using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Micro
{
    public sealed class MicroDataset
    {
        public IReadOnlyList<LabeledCausalRow> TrainRows { get; }
        public IReadOnlyList<LabeledCausalRow> MicroRows { get; }
        public DateTime TrainUntilUtc { get; }

        public TrainUntilUtc TrainUntil => new TrainUntilUtc(TrainUntilUtc);

        public MicroDataset(
            IReadOnlyList<LabeledCausalRow> trainRows,
            IReadOnlyList<LabeledCausalRow> microRows,
            DateTime trainUntilUtc)
        {
            TrainRows = trainRows ?? throw new ArgumentNullException(nameof(trainRows));
            MicroRows = microRows ?? throw new ArgumentNullException(nameof(microRows));

            if (trainUntilUtc == default)
                throw new ArgumentException("trainUntilUtc must be initialized (non-default).", nameof(trainUntilUtc));
            if (trainUntilUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof(trainUntilUtc));

            TrainUntilUtc = trainUntilUtc;
        }
    }

    public static class MicroDatasetBuilder
    {
        private static DateTime EntryUtcDt(LabeledCausalRow r) => r.EntryUtc.Value;

        public static MicroDataset Build(
            IReadOnlyList<LabeledCausalRow> allRows,
            TrainUntilUtc trainUntilUtc)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (allRows.Count == 0) throw new ArgumentException("allRows must be non-empty.", nameof(allRows));

            SeriesGuards.EnsureStrictlyAscendingUtc(allRows, r => EntryUtcDt(r), "micro-dataset.allRows");

            var ordered = allRows as List<LabeledCausalRow> ?? allRows.ToList();

            var trainUntilExitDayKeyUtc = trainUntilUtc.ExitDayKeyUtc;

            var split = NyTrainSplit.SplitByBaselineExitStrict(
                ordered,
                static r => r.EntryUtc.AsEntryUtc(),
                trainUntilExitDayKeyUtc,
                NyWindowing.NyTz,
                "micro-dataset.rows");

            var microRowsList = split.Train
                .Where(r => r.FactMicroUp || r.FactMicroDown)
                .ToList();

            var trainFrozen = split.Train.ToArray();
            var microFrozen = microRowsList.ToArray();

            return new MicroDataset(trainFrozen, microFrozen, trainUntilUtc.Value);
        }

        public static MicroDataset Build(
            IReadOnlyList<LabeledCausalRow> allRows,
            DateTime trainUntilUtc)
        {
            return Build(allRows, new TrainUntilUtc(trainUntilUtc));
        }
    }
}
