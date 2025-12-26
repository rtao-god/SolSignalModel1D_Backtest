using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Dir
{
    /// <summary>
    /// DTO для dir-датасета: только дни с фактическим ходом (Label ∈ {0,2}),
    /// разложенные на NORMAL / DOWN.
    ///
    /// Контракт времени:
    /// - TrainUntilExitDayKeyUtc — exit-day-key (DayKeyUtc)
    /// </summary>
    public sealed class DirDataset
    {
        public IReadOnlyList<LabeledCausalRow> DirNormalRows { get; }
        public IReadOnlyList<LabeledCausalRow> DirDownRows { get; }
        public DayKeyUtc TrainUntilExitDayKeyUtc { get; }

        public DirDataset(
            IReadOnlyList<LabeledCausalRow> dirNormalRows,
            IReadOnlyList<LabeledCausalRow> dirDownRows,
            DayKeyUtc trainUntilExitDayKeyUtc)
        {
            if (dirNormalRows == null) throw new ArgumentNullException(nameof(dirNormalRows));
            if (dirDownRows == null) throw new ArgumentNullException(nameof(dirDownRows));

            DirNormalRows = dirNormalRows.ToArray();
            DirDownRows = dirDownRows.ToArray();

            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException(
                    "trainUntilExitDayKeyUtc must be initialized (non-default).",
                    nameof(trainUntilExitDayKeyUtc));

            TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;
        }
    }

    /// <summary>
    /// Dataset-builder для dir-слоя.
    /// Использует DailyDatasetBuilder, отключая балансировку move.
    /// </summary>
    public static class DirDatasetBuilder
    {
        public static DirDataset Build(
            IReadOnlyList<LabeledCausalRow> allRows,
            DayKeyUtc trainUntilExitDayKeyUtc,
            bool balanceDir,
            double balanceTargetFrac,
            HashSet<DayKeyUtc>? dayKeysToExclude = null)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException(
                    "trainUntilExitDayKeyUtc must be initialized (non-default).",
                    nameof(trainUntilExitDayKeyUtc));

            var daily = DailyDatasetBuilder.Build(
                allRows: allRows as List<LabeledCausalRow> ?? allRows.ToList(),
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                balanceMove: false,
                balanceDir: balanceDir,
                balanceTargetFrac: balanceTargetFrac,
                dayKeysToExclude: dayKeysToExclude);

            return new DirDataset(
                dirNormalRows: daily.DirNormalRows,
                dirDownRows: daily.DirDownRows,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc);
        }
    }
}
