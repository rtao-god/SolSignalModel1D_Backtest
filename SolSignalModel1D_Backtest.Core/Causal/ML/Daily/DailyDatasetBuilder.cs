using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
{
    public sealed class DailyDataset
    {
        public List<LabeledCausalRow> TrainRows { get; }
        public List<LabeledCausalRow> MoveTrainRows { get; }
        public List<LabeledCausalRow> DirNormalRows { get; }
        public List<LabeledCausalRow> DirDownRows { get; }
        public DayKeyUtc TrainUntilExitDayKeyUtc { get; }

        public string TrainUntilIsoDate => TrainUntilExitDayKeyUtc.ToString();

        public DailyDataset(
            List<LabeledCausalRow> trainRows,
            List<LabeledCausalRow> moveTrainRows,
            List<LabeledCausalRow> dirNormalRows,
            List<LabeledCausalRow> dirDownRows,
            DayKeyUtc trainUntilExitDayKeyUtc)
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
        /// Каноничный контракт: boundary строго DayKeyUtc (exit-day-key).
        /// </summary>
        public static DailyDataset Build(
            List<LabeledCausalRow> allRows,
            DayKeyUtc trainUntilExitDayKeyUtc,
            bool balanceMove,
            bool balanceDir,
            double balanceTargetFrac,
            HashSet<DayKeyUtc>? dayKeysToExclude = null)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            static DateTime EntryUtcInstant(LabeledCausalRow r) => r.EntryUtc.Value;

            var ordered = allRows
                .OrderBy(EntryUtcInstant)
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: static r => new EntryUtc(r.EntryUtc.Value),
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: NyTz);

            var trainRows = split.Train is List<LabeledCausalRow> tl ? tl : split.Train.ToList();

            if (dayKeysToExclude != null && dayKeysToExclude.Count > 0)
            {
                trainRows = trainRows
                    .Where(r => !dayKeysToExclude.Contains(r.DayKeyUtc))
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

        [Obsolete("Use Build(..., DayKeyUtc trainUntilExitDayKeyUtc, ..., HashSet<DayKeyUtc>? dayKeysToExclude).")]
        public static DailyDataset Build(
            List<LabeledCausalRow> allRows,
            DateTime trainUntilUtc00,
            bool balanceMove,
            bool balanceDir,
            double balanceTargetFrac,
            HashSet<DateTime>? datesToExcludeUtc00 = null)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));

            var trainUntilDayKey = DayKeyUtc.FromUtcOrThrow(trainUntilUtc00);

            HashSet<DayKeyUtc>? dayKeysToExclude = null;
            if (datesToExcludeUtc00 != null && datesToExcludeUtc00.Count > 0)
            {
                dayKeysToExclude = new HashSet<DayKeyUtc>();
                foreach (var dt in datesToExcludeUtc00)
                    dayKeysToExclude.Add(DayKeyUtc.FromUtcOrThrow(dt));
            }

            return Build(
                allRows: allRows,
                trainUntilExitDayKeyUtc: trainUntilDayKey,
                balanceMove: balanceMove,
                balanceDir: balanceDir,
                balanceTargetFrac: balanceTargetFrac,
                dayKeysToExclude: dayKeysToExclude);
        }
    }
}
