using System;
using System.Collections.Generic;
using System.Globalization;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation
{
    /// <summary>
    /// Метаданные границы train/oos для подписи сегментов.
    /// </summary>
    public readonly struct TrainBoundaryMeta
    {
        public DayKeyUtc TrainUntilDayKeyUtc { get; }
        public string TrainUntilIsoDate { get; }

        public TrainBoundaryMeta(DayKeyUtc trainUntilDayKeyUtc)
        {
            if (trainUntilDayKeyUtc.Equals(default(DayKeyUtc)))
                throw new ArgumentException("trainUntilDayKeyUtc must be initialized (non-default).", nameof(trainUntilDayKeyUtc));

            TrainUntilDayKeyUtc = trainUntilDayKeyUtc;
            TrainUntilIsoDate = trainUntilDayKeyUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
