using System;
using System.Globalization;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation
{
    /// <summary>
    /// Метаданные границы train/oos для подписи сегментов.
    /// Контракт: граница — exit-day-key (DayKeyUtc)
    /// </summary>
    public readonly struct TrainBoundaryMeta
    {
        public DayKeyUtc TrainUntilExitDayKeyUtc { get; }
        public string TrainUntilIsoDate { get; }

        public TrainBoundaryMeta(DayKeyUtc trainUntilExitDayKeyUtc)
        {
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;
            TrainUntilIsoDate = trainUntilExitDayKeyUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
