using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.ML.Diagnostics.SL;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.ML.SL;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private static void TrainAndApplySlModelOffline(
            TrainOnly<BacktestRecord> trainRecords,
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1h> sol1h,
            IReadOnlyList<Candle1m> sol1m,
            IReadOnlyList<Candle6h> solAll6h)
        {
            if (trainRecords == null) throw new ArgumentNullException(nameof(trainRecords));
            if (trainRecords.Count == 0)
                throw new InvalidOperationException("[sl-offline] trainRecords is empty – SL-model cannot be trained.");

            if (records == null)
                throw new ArgumentNullException(nameof(records), "[sl-offline] records is null.");

            if (solAll6h == null || solAll6h.Count == 0)
                throw new InvalidOperationException("[sl-offline] solAll6h is null or empty – expected non-empty 6h series.");

            if (sol1m == null || sol1m.Count == 0)
                throw new InvalidOperationException("[sl-offline] sol1m is null or empty – 1m candles are required for SL-model.");

            if (sol1h == null || sol1h.Count == 0)
                throw new InvalidOperationException("[sl-offline] sol1h is null or empty – 1h candles are required for SL-model.");

            static DateTime EntryUtc(BacktestRecord r) => r.Causal.EntryUtc.Value;

            double[] strongMinMoveThresholds = { 0.025, 0.030, 0.035 };
            const double MainStrongMinMoveThreshold = 0.030;

            Console.WriteLine(
                $"[sl-offline] trainRecords={trainRecords.Count}, tag='{trainRecords.Tag}', " +
                $"trainUntilExitDayKey={trainRecords.TrainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

            if (records.Count > 0)
            {
                var inMin = records.Min(EntryUtc);
                var inMax = records.Max(EntryUtc);
                Console.WriteLine($"[sl-offline] input records period = {inMin:yyyy-MM-dd}..{inMax:yyyy-MM-dd}, count={records.Count}");
            }

            var slTrainMin = trainRecords.Min(EntryUtc);
            var slTrainMax = trainRecords.Max(EntryUtc);
            Console.WriteLine(
                $"[sl-offline] train period = {slTrainMin:yyyy-MM-dd}..{slTrainMax:yyyy-MM-dd}");

            var sol6hDict = solAll6h.ToDictionary(c => c.OpenTimeUtc, c => c);

            var samplesByThreshold = new Dictionary<double, List<SlHitSample>>();

            foreach (var thr in strongMinMoveThresholds)
            {
                Func<BacktestRecord, bool> strongSelector = r =>
                {
                    var mm = r.MinMove;

                    if (double.IsNaN(mm) || double.IsInfinity(mm) || mm <= 0)
                    {
                        throw new InvalidOperationException(
                            $"[sl-offline] Invalid MinMove in record: entry={EntryUtc(r):O}, MinMove={mm}.");
                    }

                    return SlStrongUtils.IsStrongByMinMove(mm, r.RegimeDown, thr);
                };

                var samples = SlOfflineBuilder.Build(
                    rows: trainRecords,
                    sol1h: sol1h,
                    sol1m: sol1m,
                    sol6hDict: sol6hDict,
                    tpPct: 0.03,
                    slPct: 0.05,
                    strongSelector: strongSelector
                );

                samplesByThreshold[thr] = samples;

                int slCountThr = samples.Count(s => s.Label);
                int tpCountThr = samples.Count - slCountThr;

                Console.WriteLine(
                    $"[sl-offline] thr={thr:0.000}: built samples = {samples.Count} (SL={slCountThr}, TP={tpCountThr})");
            }

            if (!samplesByThreshold.TryGetValue(MainStrongMinMoveThreshold, out var slSamples))
            {
                throw new InvalidOperationException(
                    $"[sl-offline] internal error: no samples for main threshold {MainStrongMinMoveThreshold:0.000}");
            }

            if (slSamples.Count < 20)
                throw new InvalidOperationException($"[sl-offline] too few samples for SL-model: {slSamples.Count} < 20.");

            var trainer = new SlFirstTrainer();
            var asOf = trainRecords.Max(EntryUtc);
            var slModel = trainer.Train(slSamples, asOf);
            var slEngine = trainer.CreateEngine(slModel);

            SlModelDiagnostics.LogFeatureImportanceOnSlModel(
                samples: slSamples,
                datasetTag: $"sl-train thr={MainStrongMinMoveThreshold:0.000}",
                modelOverride: slModel,
                featureNames: null);

            const float SlRiskThreshold = 0.55f;

            foreach (var kv in samplesByThreshold.OrderBy(kv => kv.Key))
            {
                double thr = kv.Key;
                var samples = kv.Value;

                if (samples.Count == 0)
                {
                    Console.WriteLine($"[sl-train-debug:thr={thr:0.000}] no samples, skip.");
                    continue;
                }

                PredictionEngine<SlHitSample, SlHitPrediction> engineForThisThr;

                if (Math.Abs(thr - MainStrongMinMoveThreshold) < 1e-9)
                {
                    engineForThisThr = slEngine;
                }
                else
                {
                    var tmpModel = trainer.Train(samples, asOf);
                    engineForThisThr = trainer.CreateEngine(tmpModel);
                }

                DebugSlTrainMetrics(
                    samples,
                    engineForThisThr,
                    SlRiskThreshold,
                    tag: $"thr={thr:0.000}");
            }

            int scored = 0;
            int predHighDays = 0;
            int overlayApplied = 0;

            double minProb = double.PositiveInfinity;
            double maxProb = double.NegativeInfinity;
            double sumProb = 0.0;
            int probCount = 0;

            foreach (var rec in records)
            {
                if (rec.Causal == null)
                {
                    throw new InvalidOperationException(
                        $"[sl-runtime] BacktestRecord.Causal is null for entry {EntryUtc(rec):O}.");
                }

                var causal = rec.Causal;

                // Важно: после SL-стадии значения должны быть non-null для ВСЕХ дней.
                // 0.0/false = "SL не применялся для дня" (например, нет направления/сделки).
                causal.SlProb = 0.0;
                causal.SlHighDecision = false;

                bool goLong = causal.PredLabel == 2 || (causal.PredLabel == 1 && causal.PredMicroUp);
                bool goShort = causal.PredLabel == 0 || (causal.PredLabel == 1 && causal.PredMicroDown);

                if (!goLong && !goShort)
                    continue;

                var dayMinMove = causal.MinMove;

                if (double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove) || dayMinMove <= 0)
                {
                    throw new InvalidOperationException(
                        $"[sl-runtime] Invalid MinMove in causal record: entry={EntryUtc(rec):O}, MinMove={dayMinMove}.");
                }

                bool strong = SlStrongUtils.IsStrongByMinMove(dayMinMove, causal.RegimeDown, MainStrongMinMoveThreshold);

                double entryPrice = rec.Entry;
                if (entryPrice <= 0)
                {
                    throw new InvalidOperationException(
                        $"[sl-runtime] Non-positive entry price {entryPrice} for entry {EntryUtc(rec):O}.");
                }

                var entryUtc = EntryUtc(rec);

                var slFeats = SlFeatureBuilder.Build(
                    entryUtc: entryUtc,
                    goLong: goLong,
                    strongSignal: strong,
                    dayMinMove: dayMinMove,
                    entryPrice: entryPrice,
                    candles1h: sol1h
                );

                var slPred = slEngine.Predict(new SlHitSample
                {
                    Label = false,
                    Features = slFeats,
                    EntryUtc = entryUtc
                });

                double p = slPred.Probability;
                bool predHigh = slPred.PredictedLabel && p >= SlRiskThreshold;

                causal.SlProb = p;
                causal.SlHighDecision = predHigh;

                scored++;

                SlOverlayApplier.Apply(
                    causal,
                    slProb: p,
                    goLong: goLong,
                    goShort: goShort,
                    strongSignal: strong);

                overlayApplied++;

                sumProb += p;
                probCount++;
                if (p < minProb) minProb = p;
                if (p > maxProb) maxProb = p;
                if (predHigh) predHighDays++;
            }

            if (records.Count > 0)
            {
                var recMin = records.Min(EntryUtc);
                var recMax = records.Max(EntryUtc);
                Console.WriteLine(
                    $"[sl-runtime] records period = {recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}, count = {records.Count}");
            }
            else
            {
                Console.WriteLine("[sl-runtime] records: count=0");
            }

            if (probCount > 0)
            {
                double avgProb = sumProb / probCount;
                Console.WriteLine(
                    $"[sl-runtime] scored days = {scored}/{records.Count}, " +
                    $"overlayApplied={overlayApplied}, predHigh={predHighDays}, " +
                    $"prob range = [{minProb:0.000}..{maxProb:0.000}], avg={avgProb:0.000}, " +
                    $"thr={SlRiskThreshold:0.00}, strongMinMove={MainStrongMinMoveThreshold:P1}");
            }
            else
            {
                Console.WriteLine(
                    $"[sl-runtime] scored days = {scored}/{records.Count}, " +
                    "no SL-scores produced (no trading days with direction).");
            }
        }

        private static void DebugSlTrainMetrics(
            List<SlHitSample> samples,
            PredictionEngine<SlHitSample, SlHitPrediction> engine,
            float riskThreshold,
            string tag)
        {
            int trainPos = 0;
            int trainNeg = 0;
            int trainPredHigh = 0;
            int trainTp = 0;
            int trainFp = 0;

            foreach (var s in samples)
            {
                if (s.Label) trainPos++;
                else trainNeg++;

                var pred = engine.Predict(s);
                double p = pred.Probability;
                bool high = pred.PredictedLabel && p >= riskThreshold;
                if (!high) continue;

                trainPredHigh++;
                if (s.Label) trainTp++;
                else trainFp++;
            }

            double tprTrain = trainPos > 0 ? (double)trainTp / trainPos : 0.0;
            double fprTrain = trainNeg > 0 ? (double)trainFp / trainNeg : 0.0;

            Console.WriteLine(
                $"[sl-train-debug:{tag}] pos={trainPos}, neg={trainNeg}, predHigh={trainPredHigh}, " +
                $"TPR={tprTrain:P1}, FPR={fprTrain:P1}, thr={riskThreshold:0.00}");
        }
    }
}
