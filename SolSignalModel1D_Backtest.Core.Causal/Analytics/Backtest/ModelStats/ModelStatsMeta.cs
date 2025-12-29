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
        public required ModelRunKind RunKind { get; init; }

        public required bool HasOos { get; init; }

        public required TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; init; }

        public required string TrainUntilIsoDate { get; init; }

        public required int TrainRecordsCount { get; init; }
        public required int OosRecordsCount { get; init; }
        public required int TotalRecordsCount { get; init; }

        public required int RecentDays { get; init; }
        public required int RecentRecordsCount { get; init; }
    }
}
