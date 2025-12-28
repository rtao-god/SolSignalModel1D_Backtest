using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private static BaselineExitUtc _trainUntilBaselineExitUtc;
        private static TrainUntilExitDayKeyUtc _trainUntilExitDayKeyUtc;

        private static EntryUtc ToEntryUtc(EntryUtc entry) => entry;
        private static EntryUtc ToEntryUtc(NyTradingEntryUtc entry) => entry.AsEntryUtc();

        private static PredictionEngine CreatePredictionEngineOrFallback(
            List<LabeledCausalRow> allRows
        )
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
                rows: ordered,
                holdoutDays: HoldoutDays,
                nyTz: NyTz);

            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(trainUntilUtc);

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

            const int MinTrainRows = 100;

            if (trainRows.Count < MinTrainRows)
            {
                throw new InvalidOperationException(
                    "[engine] Not enough train rows for causal training. " +
                    $"train={trainRows.Count}, min={MinTrainRows}, " +
                    $"trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}, " +
                    $"entryRange=[{minEntryUtc:yyyy-MM-dd}; {maxEntryUtc:yyyy-MM-dd}]. " +
                    "Refusing to train on full history to avoid future leakage.");
            }

            var finalTrainRows = trainRows;

            _trainUntilBaselineExitUtc = new BaselineExitUtc(new UtcInstant(trainUntilUtc));
            _trainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;

            Console.WriteLine(
                $"[engine] training on rows with baseline-exit day <= {_trainUntilExitDayKeyUtc.Value:yyyy-MM-dd} " +
                $"(train={split.Train.Count}, oos={split.Oos.Count}, total={ordered.Count}, entryRange=[{minEntryUtc:yyyy-MM-dd}; {maxEntryUtc:yyyy-MM-dd}])");

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
            IReadOnlyList<Candle1m> sol1m,
            PredictionEngine engine)
        {
            if (mornings == null) throw new ArgumentNullException(nameof(mornings));
            if (sol1m == null) throw new ArgumentNullException(nameof(sol1m));
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            if (_trainUntilBaselineExitUtc.IsDefault)
                throw new InvalidOperationException("[forward] _trainUntilBaselineExitUtc не установлен.");

            if (_trainUntilExitDayKeyUtc.IsDefault)
                throw new InvalidOperationException("[forward] _trainUntilExitDayKeyUtc не установлен.");

            var orderedMornings = mornings as List<LabeledCausalRow> ?? mornings.ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: orderedMornings,
                entrySelector: r => ToEntryUtc(r.Causal.EntryUtc),
                trainUntilExitDayKeyUtc: _trainUntilExitDayKeyUtc,
                nyTz: NyTz);

            Console.WriteLine(
                $"[forward] mornings total={orderedMornings.Count}, train={split.Train.Count}, oos={split.Oos.Count}, excluded={split.Excluded.Count}, " +
                $"trainUntilExitDayKeyUtc={_trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

            var causalRecords = new List<CausalPredictionRecord>(orderedMornings.Count);

            for (int k = 0; k < orderedMornings.Count; k++)
            {
                var r = orderedMornings[k];
                var entryUtc = r.Causal.EntryUtc.Value;

                if (!NyWindowing.TryComputeBaselineExitUtc(new EntryUtc(entryUtc), NyTz, out _))
                {
                    throw new InvalidOperationException(
                        $"[forward] baseline-exit undefined for morning entry {entryUtc:O}.");
                }

                causalRecords.Add(engine.PredictCausal(r.Causal));
            }

            var built = ForwardOutcomesBuilder.Build(
                causalRecords: causalRecords,
                truthRows: orderedMornings,
                allMinutes: sol1m);

            return await Task.FromResult(built as List<BacktestRecord> ?? built.ToList());
        }

        private static DateTime DeriveTrainUntilUtcFromHoldout(
            IReadOnlyList<LabeledCausalRow> rows,
            int holdoutDays,
            TimeZoneInfo nyTz)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0) throw new ArgumentException("rows must be non-empty.", nameof(rows));
            if (holdoutDays < 0) throw new ArgumentOutOfRangeException(nameof(holdoutDays));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var maxExitUtc = DeriveMaxBaselineExitUtc(rows, nyTz);
            var maxExitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(maxExitUtc).Value;
            var candidateExitDayKeyUtc = maxExitDayKeyUtc.AddDays(-holdoutDays);

            for (int i = 0; i < 14; i++)
            {
                var nyNoon = TimeZoneInfo.ConvertTimeFromUtc(candidateExitDayKeyUtc.AddHours(12), nyTz);
                if (nyNoon.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    var date = nyNoon.Date;
                    var noonLocal = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Unspecified);
                    bool dst = nyTz.IsDaylightSavingTime(noonLocal);
                    int morningHourLocal = dst ? 8 : 7;

                    var morningLocal = new DateTime(date.Year, date.Month, date.Day, morningHourLocal, 0, 0, DateTimeKind.Unspecified);
                    var exitLocal = morningLocal.AddMinutes(-2);
                    var exitUtc = TimeZoneInfo.ConvertTimeToUtc(exitLocal, nyTz);

                    if (exitUtc > maxExitUtc)
                    {
                        throw new InvalidOperationException(
                            $"[engine] derived trainUntilUtc exceeds max baseline-exit. trainUntilUtc={exitUtc:O}, maxExitUtc={maxExitUtc:O}, holdoutDays={holdoutDays}.");
                    }

                    return exitUtc;
                }

                candidateExitDayKeyUtc = candidateExitDayKeyUtc.AddDays(-1);
            }

            throw new InvalidOperationException(
                $"[engine] failed to derive trainUntilUtc from holdout in baseline-exit domain. holdoutDays={holdoutDays}, maxExitDayKeyUtc={maxExitDayKeyUtc:yyyy-MM-dd}.");
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

