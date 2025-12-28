using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.ModelStats
{
    /// <summary>
    /// Мета-информация для multi-segment model-stats.
    /// Контракт:
    /// - TrainUntilExitDayKeyUtc: это boundary в терминах baseline-exit day-key (00:00Z).
    /// - IsoDate дублируется как человекочитаемая подпись (yyyy-MM-dd).
    /// </summary>
    public sealed class ModelStatsMeta
    {
        public ModelRunKind RunKind { get; set; }

        public bool HasOos { get; set; }

        public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; set; }

        public string TrainUntilIsoDate { get; set; } = string.Empty;

        public int TrainRecordsCount { get; set; }
        public int OosRecordsCount { get; set; }
        public int TotalRecordsCount { get; set; }

        public int RecentDays { get; set; }
        public int RecentRecordsCount { get; set; }
    }
}
