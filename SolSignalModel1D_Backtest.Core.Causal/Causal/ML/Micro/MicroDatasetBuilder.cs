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
                .Where(r => r.MicroTruth.HasValue)
                .ToList();

            ValidateTrainBoundaryOrThrow(split.Train, trainUntilExitDayKeyUtc, NyWindowing.NyTz);

            var trainFrozen = split.Train.ToArray();
            var microFrozen = microRowsList.ToArray();

            return new MicroDataset(trainFrozen, microFrozen, trainUntilExitDayKeyUtc);
        }

        private static void ValidateTrainBoundaryOrThrow(
            IReadOnlyList<LabeledCausalRow> trainRows,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz)
        {
            if (trainRows == null) throw new ArgumentNullException(nameof(trainRows));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var violations = new List<(DateTime EntryUtc, DateTime ExitDayKeyUtc)>();

            for (int i = 0; i < trainRows.Count; i++)
            {
                var r = trainRows[i];
                var entryUtc = r.Causal.EntryUtc.Value;

                if (!NyWindowing.TryComputeBaselineExitUtc(new EntryUtc(entryUtc), nyTz, out var exitUtc))
                {
                    throw new InvalidOperationException(
                        $"[micro-dataset] baseline-exit undefined for train row. entryUtc={entryUtc:O}, dayKey={r.EntryDayKeyUtc.Value:yyyy-MM-dd}.");
                }

                var exitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value).Value;
                if (exitDayKeyUtc > trainUntilExitDayKeyUtc.Value)
                    violations.Add((entryUtc, exitDayKeyUtc));
            }

            if (violations.Count == 0)
                return;

            var top = violations
                .OrderByDescending(v => v.ExitDayKeyUtc)
                .Take(10)
                .Select(v => $"{v.EntryUtc:O} -> exitDayKey={v.ExitDayKeyUtc:yyyy-MM-dd}")
                .ToArray();

            Console.WriteLine(
                $"[micro-dataset] ПОДОЗРЕНИЕ: train включает записи за границей baseline-exit. " +
                $"count={violations.Count}, trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}, " +
                $"sample=[{string.Join(", ", top)}]");

            throw new InvalidOperationException(
                $"[micro-dataset] train boundary violated. " +
                $"count={violations.Count}, trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}.");
        }
    }
}

