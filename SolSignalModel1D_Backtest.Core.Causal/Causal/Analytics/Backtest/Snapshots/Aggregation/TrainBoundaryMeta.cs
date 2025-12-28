using System;
using System.Globalization;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Aggregation
{
    /// <summary>
    /// Метаданные границы train/oos для подписи сегментов.
    /// Контракт: граница — baseline-exit day-key (TrainUntilExitDayKeyUtc).
    /// </summary>
    public readonly struct TrainBoundaryMeta
    {
        public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; }
        public string TrainUntilIsoDate { get; }

        public TrainBoundaryMeta(TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;
            TrainUntilIsoDate = trainUntilExitDayKeyUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
