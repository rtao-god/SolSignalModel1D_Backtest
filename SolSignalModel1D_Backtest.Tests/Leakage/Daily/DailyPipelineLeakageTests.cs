using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
{
    /// <summary>
    /// Интеграционный тест: разрушение train-сигнала должно ломать качество на OOS.
    /// </summary>
    public sealed class DailyPipelineLeakageTests
    {
        private sealed class DailyRunResult
        {
            public required TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; init; }
            public required List<(DateTime DateUtc, int TrueLabel, int PredLabel)> OosPreds { get; init; }
            public required double BaselineOosAccuracy { get; init; }
        }

        [Fact]
        public async Task DailyModel_OosQualityDrops_WhenTrainLabelsAreShuffled()
        {
            var allRows = BuildSyntheticRows(
                startUtc: new DateTime(2022, 01, 01, 13, 0, 0, DateTimeKind.Utc),
                days: 720,
                seed: 123);

            var baseline = await RunDailyPipelineAsync(allRows, mutateTrain: null);

            Assert.False(double.IsNaN(baseline.BaselineOosAccuracy), "Baseline OOS accuracy is NaN.");
            Assert.True(baseline.BaselineOosAccuracy > 0.45, $"Baseline OOS accuracy too low: {baseline.BaselineOosAccuracy:0.000}");

            var shuffled = await RunDailyPipelineAsync(
                allRows,
                mutateTrain: (rows, trainUntil) => rows = ShuffleTrainLabels(rows, trainUntil, seed: 777));

            var accShuffled = ComputeAccuracy(shuffled.OosPreds);

            Assert.True(
                accShuffled < baseline.BaselineOosAccuracy - 0.15,
                $"OOS accuracy with shuffled labels did not drop enough. baseline={baseline.BaselineOosAccuracy:0.000}, shuffled={accShuffled:0.000}");
        }

        [Fact]
        public async Task DailyModel_OosQualityDrops_WhenTrainFeaturesAreRandomized()
        {
            var allRows = BuildSyntheticRows(
                startUtc: new DateTime(2022, 01, 01, 13, 0, 0, DateTimeKind.Utc),
                days: 720,
                seed: 42);

            var baseline = await RunDailyPipelineAsync(allRows, mutateTrain: null);

            Assert.False(double.IsNaN(baseline.BaselineOosAccuracy), "Baseline OOS accuracy is NaN.");
            Assert.True(baseline.BaselineOosAccuracy > 0.45, $"Baseline OOS accuracy too low: {baseline.BaselineOosAccuracy:0.000}");

            var randomized = await RunDailyPipelineAsync(
                allRows,
                mutateTrain: (rows, trainUntil) => rows = RandomizeTrainFeatures(rows, trainUntil, seed: 999));

            var accRandom = ComputeAccuracy(randomized.OosPreds);

            Assert.True(
                accRandom < baseline.BaselineOosAccuracy - 0.15 && accRandom < 0.50,
                $"OOS accuracy with randomized features is suspiciously high. baseline={baseline.BaselineOosAccuracy:0.000}, randomized={accRandom:0.000}");
        }

        private static async Task<DailyRunResult> RunDailyPipelineAsync(
            List<LabeledCausalRow> allRows,
            Func<List<LabeledCausalRow>, TrainUntilExitDayKeyUtc, List<LabeledCausalRow>>? mutateTrain)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (allRows.Count == 0) throw new InvalidOperationException("RunDailyPipelineAsync: empty allRows.");

            var ordered = allRows.OrderBy(DayKeyUtc).ToList();
            var maxEntryUtc = ordered[^1].EntryUtc.Value;

            const int HoldoutDays = 120;
            var trainUntilEntryUtc = maxEntryUtc.AddDays(-HoldoutDays);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromExitDayKeyUtc(
                NyWindowing.ComputeExitDayKeyUtc(
                    new EntryUtc(trainUntilEntryUtc),
                    NyWindowing.NyTz));

            static NyTrainSplit.EntryClass Classify(LabeledCausalRow r, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
            {
                return NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: r.Causal.EntryUtc,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyWindowing.NyTz,
                    baselineExitDayKeyUtc: out _);
            }

            var trainRows = ordered
                .Where(r => Classify(r, trainUntilExitDayKeyUtc) == NyTrainSplit.EntryClass.Train)
                .ToList();

            if (trainRows.Count < 100)
            {
                throw new InvalidOperationException(
                    $"[DailyPipelineLeakageTests] not enough train rows for leakage test: train={trainRows.Count}. " +
                    "Test setup must guarantee sufficient train/OOS split.");
            }

            if (mutateTrain != null)
            {
                ordered = mutateTrain(ordered, trainUntilExitDayKeyUtc).OrderBy(DayKeyUtc).ToList();
                trainRows = ordered
                    .Where(r => Classify(r, trainUntilExitDayKeyUtc) == NyTrainSplit.EntryClass.Train)
                    .ToList();
            }

            var trainer = new ModelTrainer
            {
                DisableMoveModel = false,
                DisableDirNormalModel = false,
                DisableDirDownModel = true,
                DisableMicroFlatModel = false
            };

            var bundle = trainer.TrainAll(trainRows);
            var engine = new PredictionEngine(bundle);

            var oos = new List<(DateTime, int, int)>();

            foreach (var r in ordered)
            {
                if (Classify(r, trainUntilExitDayKeyUtc) != NyTrainSplit.EntryClass.Oos)
                    continue;

                var p = engine.PredictCausal(r.Causal);
                oos.Add((DayKeyUtc(r), r.TrueLabel, p.PredLabel));
            }

            var acc = ComputeAccuracy(oos);

            return await Task.FromResult(new DailyRunResult
            {
                TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                OosPreds = oos,
                BaselineOosAccuracy = acc
            });
        }

        private static double ComputeAccuracy(List<(DateTime DateUtc, int TrueLabel, int PredLabel)> preds)
        {
            int total = preds.Count;
            if (total == 0) return double.NaN;

            int ok = 0;
            foreach (var x in preds)
            {
                if (x.PredLabel == x.TrueLabel) ok++;
            }

            return ok / (double)total;
        }

        private static List<LabeledCausalRow> ShuffleTrainLabels(List<LabeledCausalRow> rows, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc, int seed)
        {
            var trainIdx = new List<int>();

            for (int i = 0; i < rows.Count; i++)
            {
                if (NyTrainSplit.ClassifyByBaselineExit(
                        entryUtc: rows[i].Causal.EntryUtc,
                        trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                        nyTz: NyWindowing.NyTz,
                        baselineExitDayKeyUtc: out _) == NyTrainSplit.EntryClass.Train)
                    trainIdx.Add(i);
            }

            if (trainIdx.Count == 0)
                throw new InvalidOperationException("[shuffle-labels] train-set is empty.");

            var labels = trainIdx.Select(idx => rows[idx].TrueLabel).ToArray();

            var rng = new Random(seed);
            for (int i = labels.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (labels[i], labels[j]) = (labels[j], labels[i]);
            }

            var res = new List<LabeledCausalRow>(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (NyTrainSplit.ClassifyByBaselineExit(
                        entryUtc: r.Causal.EntryUtc,
                        trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                        nyTz: NyWindowing.NyTz,
                        baselineExitDayKeyUtc: out _) != NyTrainSplit.EntryClass.Train)
                {
                    res.Add(r);
                    continue;
                }

                int pos = trainIdx.IndexOf(i);
                int newLabel = labels[pos];

                res.Add(new LabeledCausalRow(
                    causal: r.Causal,
                    trueLabel: newLabel,
                    factMicroUp: r.FactMicroUp,
                    factMicroDown: r.FactMicroDown));
            }

            return res;
        }

        private static List<LabeledCausalRow> RandomizeTrainFeatures(List<LabeledCausalRow> rows, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc, int seed)
        {
            var rng = new Random(seed);

            var res = new List<LabeledCausalRow>(rows.Count);

            foreach (var r in rows)
            {
                if (NyTrainSplit.ClassifyByBaselineExit(
                        entryUtc: r.Causal.EntryUtc,
                        trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                        nyTz: NyWindowing.NyTz,
                        baselineExitDayKeyUtc: out _) != NyTrainSplit.EntryClass.Train)
                {
                    res.Add(r);
                    continue;
                }

                var c = r.Causal;

                double NextRand() => rng.NextDouble() * 2.0 - 1.0;

                var randomized = new CausalDataRow(
                    entryUtc: c.TradingEntryUtc,
                    regimeDown: c.RegimeDown,
                    isMorning: c.IsMorning,
                    hardRegime: c.HardRegime,
                    minMove: c.MinMove,

                    solRet30: NextRand(),
                    btcRet30: NextRand(),
                    solBtcRet30: NextRand(),

                    solRet1: NextRand(),
                    solRet3: NextRand(),
                    btcRet1: NextRand(),
                    btcRet3: NextRand(),

                    fngNorm: NextRand(),
                    dxyChg30: NextRand(),
                    goldChg30: NextRand(),

                    btcVs200: NextRand(),

                    solRsiCenteredScaled: NextRand(),
                    rsiSlope3Scaled: NextRand(),

                    gapBtcSol1: NextRand(),
                    gapBtcSol3: NextRand(),

                    atrPct: Math.Abs(NextRand()) * 0.05 + 0.001,
                    dynVol: Math.Abs(NextRand()) * 2.0 + 0.1,

                    solAboveEma50: NextRand(),
                    solEma50vs200: NextRand() * 0.05,
                    btcEma50vs200: NextRand() * 0.05);

                res.Add(new LabeledCausalRow(
                    causal: randomized,
                    trueLabel: r.TrueLabel,
                    factMicroUp: r.FactMicroUp,
                    factMicroDown: r.FactMicroDown));
            }

            return res;
        }

        private static List<LabeledCausalRow> BuildSyntheticRows(DateTime startUtc, int days, int seed)
        {
            if (startUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException("startUtc must be UTC.");

            var rng = new Random(seed);

            var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(startUtc.Year, startUtc.Month, startUtc.Day, 0, 0),
                count: days,
                hour: 8);

            var rows = new List<LabeledCausalRow>(datesUtc.Count);

            for (int i = 0; i < datesUtc.Count; i++)
            {
                var entryUtcRaw = datesUtc[i];

                if (!NyWindowing.TryCreateNyTradingEntryUtc(new EntryUtc(entryUtcRaw), NyWindowing.NyTz, out var entryUtc))
                    throw new InvalidOperationException($"[DailyPipelineLeakageTests] NyTradingEntryUtc expected, got {entryUtcRaw:O}");

                double solRet1 = (rng.NextDouble() - 0.5) * 0.10;

                int label =
                    solRet1 > 0.01 ? 2 :
                    solRet1 < -0.01 ? 0 :
                    1;

                bool regimeDown = (label == 0);

                bool factMicroUp = false;
                bool factMicroDown = false;

                if (label == 1)
                {
                    factMicroUp = rng.NextDouble() < 0.5;
                    factMicroDown = !factMicroUp;
                }

                var causal = new CausalDataRow(
                    entryUtc: entryUtc,
                    regimeDown: regimeDown,
                    isMorning: true,
                    hardRegime: 1,
                    minMove: 0.02,

                    solRet30: solRet1 * 0.2,
                    btcRet30: 0.0,
                    solBtcRet30: 0.0,

                    solRet1: solRet1,
                    solRet3: solRet1 * 0.7,
                    btcRet1: solRet1 * 0.1,
                    btcRet3: solRet1 * 0.05,

                    fngNorm: 0.0,
                    dxyChg30: 0.0,
                    goldChg30: 0.0,

                    btcVs200: 0.0,

                    solRsiCenteredScaled: 0.0,
                    rsiSlope3Scaled: 0.0,

                    gapBtcSol1: 0.0,
                    gapBtcSol3: 0.0,

                    atrPct: 0.02 + rng.NextDouble() * 0.02,
                    dynVol: 0.5 + rng.NextDouble() * 1.0,

                    solAboveEma50: 1.0,
                    solEma50vs200: 0.01,
                    btcEma50vs200: 0.01);

                rows.Add(new LabeledCausalRow(
                    causal: causal,
                    trueLabel: label,
                    factMicroUp: factMicroUp,
                    factMicroDown: factMicroDown));
            }

            return rows.OrderBy(DayKeyUtc).ToList();
        }

        private static DateTime DayKeyUtc(LabeledCausalRow r) => r.Causal.EntryDayKeyUtc.Value;
    }
}
