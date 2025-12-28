using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily
{
    public sealed class DailyDataset
    {
        public List<LabeledCausalRow> TrainRows { get; }
        public List<LabeledCausalRow> MoveTrainRows { get; }
        public List<LabeledCausalRow> DirNormalRows { get; }
        public List<LabeledCausalRow> DirDownRows { get; }

        public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; }

        public string TrainUntilIsoDate => TrainUntilExitDayKeyUtc.ToString();

        public DailyDataset(
            List<LabeledCausalRow> trainRows,
            List<LabeledCausalRow> moveTrainRows,
            List<LabeledCausalRow> dirNormalRows,
            List<LabeledCausalRow> dirDownRows,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            TrainRows = trainRows ?? throw new ArgumentNullException(nameof(trainRows));
            MoveTrainRows = moveTrainRows ?? throw new ArgumentNullException(nameof(moveTrainRows));
            DirNormalRows = dirNormalRows ?? throw new ArgumentNullException(nameof(dirNormalRows));
            DirDownRows = dirDownRows ?? throw new ArgumentNullException(nameof(dirDownRows));

            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;
        }
    }

    public static class DailyDatasetBuilder
    {
        private static readonly TimeZoneInfo NyTz = NyWindowing.NyTz;

        /// <summary>
        /// Каноничный контракт:
        /// - boundary: TrainUntilExitDayKeyUtc (exit-day-key);
        /// - исключения: EntryDayKeyUtc (entry-day-key).
        /// </summary>
        public static DailyDataset Build(
            List<LabeledCausalRow> allRows,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            bool balanceMove,
            bool balanceDir,
            double balanceTargetFrac,
            HashSet<EntryDayKeyUtc>? dayKeysToExclude = null)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            static DateTime EntryUtcInstant(LabeledCausalRow r) => r.EntryUtc.Value;

            var ordered = allRows
                .OrderBy(EntryUtcInstant)
                .ToList();

            var trainRows = new List<LabeledCausalRow>(ordered.Count);

            for (int i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];

                var cls = NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: new EntryUtc(r.EntryUtc.Value),
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyTz,
                    baselineExitDayKeyUtc: out _);

                if (cls == NyTrainSplit.EntryClass.Train)
                    trainRows.Add(r);
            }

            if (dayKeysToExclude != null && dayKeysToExclude.Count > 0)
            {
                trainRows = trainRows
                    .Where(r => !dayKeysToExclude.Contains(r.EntryDayKeyUtc))
                    .ToList();
            }

            DailyTrainingDataBuilder.Build(
                trainRows: trainRows,
                balanceMove: balanceMove,
                balanceDir: balanceDir,
                balanceTargetFrac: balanceTargetFrac,
                moveTrainRows: out var moveTrainRows,
                dirNormalRows: out var dirNormalRows,
                dirDownRows: out var dirDownRows);

            return new DailyDataset(
                trainRows: trainRows,
                moveTrainRows: moveTrainRows,
                dirNormalRows: dirNormalRows,
                dirDownRows: dirDownRows,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc);
        }

        [Obsolete("Use Build(..., TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc, ..., HashSet<EntryDayKeyUtc>? dayKeysToExclude).")]
        public static DailyDataset Build(
            List<LabeledCausalRow> allRows,
            DateTime trainUntilUtc00,
            bool balanceMove,
            bool balanceDir,
            double balanceTargetFrac,
            HashSet<DateTime>? datesToExcludeUtc00 = null)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));

            var trainUntilExitDayKey = TrainUntilExitDayKeyUtc.FromUtcOrThrow(trainUntilUtc00);

            HashSet<EntryDayKeyUtc>? dayKeysToExclude = null;
            if (datesToExcludeUtc00 != null && datesToExcludeUtc00.Count > 0)
            {
                dayKeysToExclude = new HashSet<EntryDayKeyUtc>();
                foreach (var dt in datesToExcludeUtc00)
                    dayKeysToExclude.Add(EntryDayKeyUtc.FromUtcMomentOrThrow(dt));
            }

            return Build(
                allRows: allRows,
                trainUntilExitDayKeyUtc: trainUntilExitDayKey,
                balanceMove: balanceMove,
                balanceDir: balanceDir,
                balanceTargetFrac: balanceTargetFrac,
                dayKeysToExclude: dayKeysToExclude);
        }
    }
}

