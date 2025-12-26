using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private static DateTime _trainUntilUtc;
        private static DayKeyUtc _trainUntilExitDayKeyUtc;

        private static EntryUtc ToEntryUtc(EntryUtc entry) => entry;
        private static EntryUtc ToEntryUtc(NyTradingEntryUtc entry) => entry.AsEntryUtc();

        private static PredictionEngine CreatePredictionEngineOrFallback(List<LabeledCausalRow> allRows)
        {
            PredictionEngine.DebugAllowDisabledModels = false;

            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (allRows.Count == 0)
                throw new InvalidOperationException("[engine] Пустой список LabeledCausalRow для обучения моделей");

            var ordered = allRows
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            var minEntryUtc = ordered.First().Causal.EntryUtc.Value;
            var maxEntryUtc = ordered.Last().Causal.EntryUtc.Value;

            const int HoldoutDays = 120;

            var trainUntilUtc = DeriveTrainUntilUtcFromHoldout(
                maxEntryUtc: maxEntryUtc,
                holdoutDays: HoldoutDays,
                nyTz: NyTz);

            var trainUntilExitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc);

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
               entrySelector: r => ToEntryUtc(r.Causal.EntryUtc),
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: NyTz);

            var trainRows = split.Train;

            if (split.Excluded.Count > 0)
            {
                Console.WriteLine(
                    $"[engine] WARNING: excluded={split.Excluded.Count} rows because baseline-exit is undefined by contract.");
            }

            var labelHist = trainRows
                .GroupBy(r => r.TrueLabel)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}")
                .ToArray();

            Console.WriteLine("[engine] train label hist: " + string.Join(", ", labelHist));

            if (labelHist.Length <= 1)
                Console.WriteLine("[engine] WARNING: train labels are degenerate (<=1 class).");

            IReadOnlyList<LabeledCausalRow> finalTrainRows;

            if (trainRows.Count < 100)
            {
                Console.WriteLine(
                    $"[engine] trainRows too small ({trainRows.Count}), используем всю историю без hold-out.");

                finalTrainRows = ordered;

                _trainUntilUtc = DeriveMaxBaselineExitUtc(rows: ordered, nyTz: NyTz);
                _trainUntilExitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(_trainUntilUtc);
            }
            else
            {
                finalTrainRows = trainRows;

                _trainUntilUtc = trainUntilUtc;
                _trainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;

                Console.WriteLine(
                    $"[engine] training on rows with baseline-exit day <= {_trainUntilExitDayKeyUtc.Value:yyyy-MM-dd} " +
                    $"(train={split.Train.Count}, oos={split.Oos.Count}, total={ordered.Count}, entryRange=[{minEntryUtc:yyyy-MM-dd}; {maxEntryUtc:yyyy-MM-dd}])");
            }

            var trainer = new ModelTrainer
            {
                DisableMoveModel = false,
                DisableDirNormalModel = false,
                DisableDirDownModel = false,
                DisableMicroFlatModel = false
            };

            var bundle = trainer.TrainAll(finalTrainRows);

            if (bundle.MlCtx == null)
                throw new InvalidOperationException("[engine] ModelTrainer вернул ModelBundle с MlCtx == null");

            Console.WriteLine(
                "[engine] ModelBundle trained: move+dir " +
                (bundle.MicroFlatModel != null ? "+ micro" : "(без микро-слоя)"));

            return new PredictionEngine(bundle);
        }

        private static async Task<List<BacktestRecord>> LoadPredictionRecordsAsync(
            IReadOnlyList<LabeledCausalRow> mornings,
            IReadOnlyList<Candle6h> solAll6h,
            PredictionEngine engine)
        {
            if (mornings == null) throw new ArgumentNullException(nameof(mornings));
            if (solAll6h == null) throw new ArgumentNullException(nameof(solAll6h));
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            if (_trainUntilUtc == default)
                throw new InvalidOperationException("[forward] _trainUntilUtc не установлен.");

            if (_trainUntilExitDayKeyUtc.IsDefault)
                throw new InvalidOperationException("[forward] _trainUntilExitDayKeyUtc не установлен.");

            var sorted6h = solAll6h is List<Candle6h> list6h ? list6h : solAll6h.ToList();
            if (sorted6h.Count == 0)
                throw new InvalidOperationException("[forward] Пустая серия 6h для SOL");

            var indexByOpenTime = new Dictionary<DateTime, int>(sorted6h.Count);
            for (int i = sorted6h.Count - 1; i >= 0; i--)
                indexByOpenTime[sorted6h[i].OpenTimeUtc] = i;

            var orderedMornings = mornings as List<LabeledCausalRow> ?? mornings.ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: orderedMornings,
                entrySelector: r => ToEntryUtc(r.Causal.EntryUtc),
                trainUntilExitDayKeyUtc: _trainUntilExitDayKeyUtc,
                nyTz: NyTz);

            Console.WriteLine(
                $"[forward] mornings total={orderedMornings.Count}, train={split.Train.Count}, oos={split.Oos.Count}, excluded={split.Excluded.Count}, " +
                $"trainUntilExitDayKeyUtc={_trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

            var list = new List<BacktestRecord>(orderedMornings.Count);

            for (int k = 0; k < orderedMornings.Count; k++)
            {
                var r = orderedMornings[k];

                var entryUtc = r.Causal.EntryUtc.Value;
                var entry = ToEntryUtc(r.Causal.EntryUtc);

                if (!NyWindowing.TryComputeBaselineExitUtc(entry, NyTz, out var exitUtc))
                {
                    Console.WriteLine(
                        $"[forward] skip entry {entryUtc:O}: baseline-exit undefined by contract.");
                    continue;
                }

                var exitUtcDt = exitUtc.Value;

                var causal = engine.PredictCausal(r.Causal);

                if (!indexByOpenTime.TryGetValue(entryUtc, out var entryIdx))
                    throw new InvalidOperationException($"[forward] entry candle {entryUtc:O} not found in 6h series");

                var exitIdx = -1;
                for (int i = entryIdx; i < sorted6h.Count; i++)
                {
                    var start = sorted6h[i].OpenTimeUtc;
                    var end = (i + 1 < sorted6h.Count) ? sorted6h[i + 1].OpenTimeUtc : start.AddHours(6);

                    if (exitUtcDt >= start && exitUtcDt < end)
                    {
                        exitIdx = i;
                        break;
                    }
                }

                if (exitIdx < 0)
                    throw new InvalidOperationException($"[forward] no 6h candle covering baseline exit {exitUtcDt:O} (entry {entryUtc:O})");

                if (exitIdx <= entryIdx)
                    throw new InvalidOperationException($"[forward] exitIdx {exitIdx} <= entryIdx {entryIdx} for entry {entryUtc:O}");

                var entryPrice = sorted6h[entryIdx].Close;

                double maxHigh = double.MinValue;
                double minLow = double.MaxValue;

                for (int j = entryIdx + 1; j <= exitIdx; j++)
                {
                    var c = sorted6h[j];
                    if (c.High > maxHigh) maxHigh = c.High;
                    if (c.Low < minLow) minLow = c.Low;
                }

                if (maxHigh == double.MinValue || minLow == double.MaxValue)
                    throw new InvalidOperationException($"[forward] no candles between entry {entryUtc:O} and exit {exitUtcDt:O}");

                var fwdClose = sorted6h[exitIdx].Close;

                var forward = new ForwardOutcomes
                {
                    EntryUtc = entry,

                    TrueLabel = r.TrueLabel,
                    FactMicroUp = r.FactMicroUp,
                    FactMicroDown = r.FactMicroDown,

                    Entry = entryPrice,
                    MaxHigh24 = maxHigh,
                    MinLow24 = minLow,
                    Close24 = fwdClose,

                    MinMove = r.Causal.MinMove,

                    WindowEndUtc = exitUtcDt,
                    DayMinutes = Array.Empty<Candle1m>()
                };

                list.Add(new BacktestRecord { Causal = causal, Forward = forward });
            }

            return await Task.FromResult(list);
        }

        private static DateTime DeriveTrainUntilUtcFromHoldout(DateTime maxEntryUtc, int holdoutDays, TimeZoneInfo nyTz)
        {
            if (holdoutDays < 0) throw new ArgumentOutOfRangeException(nameof(holdoutDays));
            if (maxEntryUtc == default) throw new ArgumentException("maxEntryUtc must be initialized.", nameof(maxEntryUtc));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var candidateEntry = maxEntryUtc.AddDays(-holdoutDays);

            for (int i = 0; i < 14; i++)
            {
                var ny = TimeZoneInfo.ConvertTimeFromUtc(candidateEntry, nyTz);
                if (ny.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    return NyWindowing.ComputeBaselineExitUtc(new EntryUtc(candidateEntry), nyTz).Value;

                candidateEntry = candidateEntry.AddDays(-1);
            }

            throw new InvalidOperationException("[engine] failed to derive trainUntilUtc from holdout.");
        }

        private static DateTime DeriveMaxBaselineExitUtc(IReadOnlyList<LabeledCausalRow> rows, TimeZoneInfo nyTz)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0) throw new ArgumentException("rows must be non-empty.", nameof(rows));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            bool hasAny = false;
            DateTime maxExit = default;

            for (int i = 0; i < rows.Count; i++)
            {
                var entryUtc = rows[i].Causal.EntryUtc.Value;
                var ny = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, nyTz);

                if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                var exitUtc = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), nyTz).Value;

                if (!hasAny || exitUtc > maxExit)
                {
                    maxExit = exitUtc;
                    hasAny = true;
                }
            }

            if (!hasAny)
                throw new InvalidOperationException("[engine] failed to derive max baseline-exit: no working-day entries found.");

            return maxExit;
        }
    }
}
