using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Diagnostics
{
    public enum BacktestDiagnosticsSegmentKind
    {
        Train = 0,
        Oos = 1,
        Recent = 2,
        Full = 3
    }

    public sealed class BacktestDiagnosticsSnapshot
    {
        public required BacktestDiagnosticsMeta Meta { get; init; }
        public required BacktestDiagnosticsCoverage Coverage { get; init; }
        public required IReadOnlyList<BacktestDiagnosticsSegmentSnapshot> Segments { get; init; }

        public required AggregationProbsSnapshot AggregationProbs { get; init; }
        public required AggregationMetricsSnapshot AggregationMetrics { get; init; }
        public required MicroStatsSnapshot MicroStats { get; init; }
        public required BacktestModelStatsMultiSnapshot ModelStats { get; init; }
    }

    public sealed class BacktestDiagnosticsMeta
    {
        public required TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; init; }
        public required int RecentDays { get; init; }
        public required int DebugLastDays { get; init; }
        public required double? ShuffleSanityAccuracyPct { get; init; }
        public required int ShuffleSanityN { get; init; }
    }

    public sealed class BacktestDiagnosticsCoverage
    {
        public required int RecordsTotal { get; init; }
        public required int RecordsExcludedByWindowing { get; init; }
        public required int TruthDailyLabelAvailable { get; init; }
        public required int MicroTruthAvailable { get; init; }
        public required int MicroGatingDays { get; init; }
        public required int SlScoreAvailable { get; init; }
        public required int SlLabelAvailable { get; init; }
        public required int SlEvalBase { get; init; }
        public required IReadOnlyDictionary<string, int> MissingReasons { get; init; }
    }

    public sealed class BacktestDiagnosticsSegmentSnapshot
    {
        public required BacktestDiagnosticsSegmentKind Kind { get; init; }
        public required string Label { get; init; }
        public required DateTime? EntryFromUtc { get; init; }
        public required DateTime? EntryToUtc { get; init; }
        public required DateTime? ExitFromUtc { get; init; }
        public required DateTime? ExitToUtc { get; init; }
        public required int RecordsCount { get; init; }
        public required BacktestDiagnosticsSegmentBases Bases { get; init; }
        public required BacktestDiagnosticsMissingBreakdown Missing { get; init; }
        public required BacktestDiagnosticsComponentStatsSnapshot ComponentStats { get; init; }
    }

    public sealed class BacktestDiagnosticsSegmentBases
    {
        public required int NDailyEval { get; init; }
        public required int NTrendEval { get; init; }
        public required int NMicroEval { get; init; }
        public required int NMicroTruth { get; init; }
        public required int NMicroGating { get; init; }
        public required int NSlEval { get; init; }
        public required int NSlScore { get; init; }
        public required int NSlLabel { get; init; }
    }

    public sealed class BacktestDiagnosticsMissingBreakdown
    {
        public required IReadOnlyDictionary<string, int> Reasons { get; init; }
    }
}
