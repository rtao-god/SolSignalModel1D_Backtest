using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Micro
{
    public sealed class MicroDataset
    {
        public IReadOnlyList<LabeledCausalRow> TrainRows { get; }
        public IReadOnlyList<LabeledCausalRow> MicroRows { get; }
        public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; }

        public MicroDataset(
            IReadOnlyList<LabeledCausalRow> trainRows,
            IReadOnlyList<LabeledCausalRow> microRows,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            TrainRows = trainRows ?? throw new ArgumentNullException(nameof(trainRows));
            MicroRows = microRows ?? throw new ArgumentNullException(nameof(microRows));

            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;
        }
    }

    public static class MicroDatasetBuilder
    {
        private static DateTime EntryUtcDt(LabeledCausalRow r) => r.EntryUtc.Value;

        public static MicroDataset Build(
            IReadOnlyList<LabeledCausalRow> allRows,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (allRows.Count == 0) throw new ArgumentException("allRows must be non-empty.", nameof(allRows));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            SeriesGuards.EnsureStrictlyAscendingUtc(allRows, r => EntryUtcDt(r), "micro-dataset.allRows");

            var ordered = allRows as List<LabeledCausalRow> ?? allRows.ToList();

            Console.WriteLine(
                $"[micro-dataset] запуск SplitByBaselineExitStrict: тег='micro-dataset.rows', trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

            var split = NyTrainSplit.SplitByBaselineExitStrict(
                ordered,
                static r => r.EntryUtc,
                trainUntilExitDayKeyUtc,
                NyWindowing.NyTz,
                "micro-dataset.rows");

            var microRowsList = split.Train
                .Where(r => r.FactMicroUp || r.FactMicroDown)
                .ToList();

            var trainFrozen = split.Train.ToArray();
            var microFrozen = microRowsList.ToArray();

            return new MicroDataset(trainFrozen, microFrozen, trainUntilExitDayKeyUtc);
        }
    }
}

