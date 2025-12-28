using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Omniscient.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed.Trainers;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private enum DelayedResultCode
        {
            None = 0,
            TpFirst = 1,
            SlFirst = 2,
            Ambiguous = 3
        }

        private static void PopulateDelayedA(
            IList<BacktestRecord> records,
            List<LabeledCausalRow> allRows,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            IReadOnlyList<Candle1h> sol1h,
            IReadOnlyList<Candle6h> solAll6h,
            IReadOnlyList<Candle1m> sol1m,
            double dipFrac = 0.005,
            double tpPct = 0.010,
            double slPct = 0.010)
        {
            if (records == null)
                throw new ArgumentNullException(nameof(records), "[PopulateDelayedA] records is null.");

            if (records.Count == 0)
                return;

            if (allRows == null || allRows.Count == 0)
                throw new InvalidOperationException("[PopulateDelayedA] allRows is null or empty – cannot build pullback dataset.");

            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            if (sol1m == null || sol1m.Count == 0)
                throw new InvalidOperationException("[PopulateDelayedA] sol1m is null or empty – 1m candles are required for delayed A.");

            if (sol1h == null || sol1h.Count == 0)
                throw new InvalidOperationException("[PopulateDelayedA] sol1h is null or empty – 1h candles are required for delayed A.");

            if (solAll6h == null || solAll6h.Count == 0)
                throw new InvalidOperationException("[PopulateDelayedA] solAll6h is null or empty – expected non-empty 6h series.");

            var sol6hDict = solAll6h.ToDictionary(c => c.OpenTimeUtc, c => c);

            var recordsRo = records as IReadOnlyList<BacktestRecord> ?? records.ToList();

            var ordered = recordsRo
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            Console.WriteLine(
                $"[delayed-A] запуск SplitByBaselineExitStrict: тег='delayed-a.records', trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

            var split = NyTrainSplit.SplitByBaselineExitStrict(
                ordered: ordered,
                entrySelector: static r => r.Causal.EntryUtc,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: NyWindowing.NyTz,
                tag: "delayed-a.records");

            if (split.Train.Count == 0)
                throw new InvalidOperationException("[PopulateDelayedA] No train records for delayed-A model (split.Train is empty).");

            List<PullbackContinuationSample> pullbackSamples = PullbackContinuationOfflineBuilder.Build(
                rows: split.Train,
                sol1h: sol1h,
                sol6hDict: sol6hDict
            );

            Console.WriteLine($"[PopulateDelayedA] built pullbackSamples = {pullbackSamples.Count}");

            if (pullbackSamples.Count == 0)
                throw new InvalidOperationException("[PopulateDelayedA] No samples for model A – check input rows and candles consistency.");

            var pullbackTrainer = new PullbackContinuationTrainer();
            DateTime asOfDate = trainUntilExitDayKeyUtc.Value.AddDays(1);
            var pullbackModel = pullbackTrainer.Train(pullbackSamples, asOfDate);
            var pullbackEngine = pullbackTrainer.CreateEngine(pullbackModel);

            foreach (var rec in records)
            {
                if (rec == null)
                    throw new InvalidOperationException("[PopulateDelayedA] records contains null item.");

                // required init, но оставляем явную диагностику.
                if (rec.Causal == null)
                {
                    throw new InvalidOperationException(
                        $"[PopulateDelayedA] BacktestRecord.Causal is null for entry {CausalTimeKey.EntryUtc(rec).Value:O}.");
                }

                var causal = rec.Causal;

                // Повторный вызов должен быть идемпотентным: чистим предыдущее исполнение.
                ResetDelayedAState(rec, causal);

                // Канонический entry момента дня (UTC).
                var entry = CausalTimeKey.EntryUtc(rec);
                DateTime dayStart = entry.Value;

                bool wantLong = causal.PredLabel == 2 || (causal.PredLabel == 1 && causal.PredMicroUp);
                bool wantShort = causal.PredLabel == 0 || (causal.PredLabel == 1 && causal.PredMicroDown);

                if (wantLong == wantShort)
                {
                    if (!wantLong)
                    {
                        causal.DelayedWhyNot = "no signal";
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"[PopulateDelayedA] Ambiguous direction: wantLong && wantShort for {dayStart:O}. " +
                        $"PredLabel={causal.PredLabel}, PredMicroUp={causal.PredMicroUp}, PredMicroDown={causal.PredMicroDown}");
                }

                if (causal.SlHighDecision != true)
                {
                    causal.DelayedWhyNot = "sl gate";
                    continue;
                }

                DateTime dayEnd;
                try
                {
                    // NyWindowing contract: entry -> baseline-exit (UTC).
                    dayEnd = NyWindowing.ComputeBaselineExitUtc(entry, NyWindowing.NyTz).Value;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[PopulateDelayedA] Failed to compute baseline exit for entry={dayStart:O}.",
                        ex);
                }

                bool strongSignal = (causal.PredLabel == 2 || causal.PredLabel == 0);

                if (double.IsNaN(causal.MinMove) || double.IsInfinity(causal.MinMove) || causal.MinMove <= 0.0)
                    throw new InvalidOperationException($"[PopulateDelayedA] MinMove must be finite and positive for {dayStart:O}.");

                double dayMinMove = causal.MinMove;

                var dayHours = sol1h
                    .Where(h => h.OpenTimeUtc >= dayStart && h.OpenTimeUtc < dayEnd)
                    .OrderBy(h => h.OpenTimeUtc)
                    .ToList();

                if (dayHours.Count == 0)
                    throw new InvalidOperationException($"[PopulateDelayedA] No 1h candles in baseline window for {dayStart:O}.");

                var features = TargetLevelFeatureBuilder.Build(
                    dayStart,
                    wantLong,
                    strongSignal,
                    dayMinMove,
                    rec.Entry,
                    sol1h
                );

                var pullbackSample = new PullbackContinuationSample
                {
                    Features = features,
                    Label = false,
                    EntryUtc = dayStart
                };

                var predA = pullbackEngine.Predict(pullbackSample);

                if (!predA.PredictedLabel || predA.Probability < 0.70f)
                {
                    causal.DelayedWhyNot = "model gate";
                    continue;
                }

                causal.DelayedSource = "A";
                causal.DelayedEntryAsked = true;
                causal.DelayedEntryUsed = true;

                var dayMinutes = sol1m
                    .Where(m => m.OpenTimeUtc >= dayStart && m.OpenTimeUtc < dayEnd)
                    .OrderBy(m => m.OpenTimeUtc)
                    .ToList();

                if (dayMinutes.Count == 0)
                    throw new InvalidOperationException($"[PopulateDelayedA] No 1m candles in baseline window for {dayStart:O}.");

                double triggerPrice = wantLong
                    ? rec.Entry * (1.0 - dipFrac)
                    : rec.Entry * (1.0 + dipFrac);

                if (double.IsNaN(triggerPrice) || double.IsInfinity(triggerPrice) || triggerPrice <= 0.0)
                    throw new InvalidOperationException($"[PopulateDelayedA] triggerPrice invalid for {dayStart:O}: {triggerPrice}.");

                DateTime maxDelayTime = dayStart.AddHours(4);
                Candle1m? fillBar = null;

                foreach (var m in dayMinutes)
                {
                    if (m.OpenTimeUtc > maxDelayTime)
                        break;

                    if (wantLong && m.Low <= triggerPrice) { fillBar = m; break; }
                    if (wantShort && m.High >= triggerPrice) { fillBar = m; break; }
                }

                if (fillBar == null)
                {
                    causal.DelayedWhyNot = "no trigger";
                    continue;
                }

                if (fillBar.OpenTimeUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[PopulateDelayedA] fillBar.OpenTimeUtc must be UTC, got {fillBar.OpenTimeUtc:O}.");

                DateTime executedAtUtc = fillBar.OpenTimeUtc;

                double effectiveTpPct = tpPct;
                double effectiveSlPct = slPct;

                if (causal.MinMove > 0.0)
                {
                    double linkedTp = causal.MinMove * 1.2;
                    if (linkedTp > effectiveTpPct)
                        effectiveTpPct = linkedTp;
                }

                causal.DelayedIntradayTpPct = effectiveTpPct;
                causal.DelayedIntradaySlPct = effectiveSlPct;

                double tpLevel = wantLong
                    ? triggerPrice * (1.0 + effectiveTpPct)
                    : triggerPrice * (1.0 - effectiveTpPct);

                double slLevel = wantLong
                    ? triggerPrice * (1.0 - effectiveSlPct)
                    : triggerPrice * (1.0 + effectiveSlPct);

                var intradayResult = DelayedIntradayResult.None;

                foreach (var m in dayMinutes)
                {
                    if (m.OpenTimeUtc < executedAtUtc)
                        continue;

                    bool hitTp = wantLong ? (m.High >= tpLevel) : (m.Low <= tpLevel);
                    bool hitSl = wantLong ? (m.Low <= slLevel) : (m.High >= slLevel);

                    if (hitTp && hitSl) { intradayResult = DelayedIntradayResult.Ambiguous; break; }
                    if (hitTp) { intradayResult = DelayedIntradayResult.TpFirst; break; }
                    if (hitSl) { intradayResult = DelayedIntradayResult.SlFirst; break; }
                }

                rec.DelayedExecution = DelayedExecutionFacts.Create(
                    executedAtUtc: executedAtUtc,
                    entryPrice: triggerPrice,
                    intradayResult: intradayResult);
            }

            static void ResetDelayedAState(BacktestRecord rec, CausalPredictionRecord causal)
            {
                causal.DelayedSource = null;
                causal.DelayedEntryAsked = false;
                causal.DelayedEntryUsed = false;
                causal.DelayedWhyNot = null;

                // Отсутствие исполнения = null.
                rec.DelayedExecution = null;

                causal.DelayedIntradayTpPct = null;
                causal.DelayedIntradaySlPct = null;
            }
        }
    }
}

