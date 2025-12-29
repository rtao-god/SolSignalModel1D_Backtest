using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Diagnostics
{
    public sealed class BacktestDiagnosticsComponentStatsSnapshot
    {
        public required TriClassComponentStats Day { get; init; }
        public required TriClassComponentStats DayMicro { get; init; }
        public required TriClassComponentStats Total { get; init; }
        public required MoveComponentStats Move { get; init; }
        public required DirComponentStats Dir { get; init; }
        public required OptionalValue<MicroComponentStats> Micro { get; init; }
    }

    public sealed class TriClassComponentStats
    {
        public required int N { get; init; }
        public required int Correct { get; init; }
        public required double Accuracy { get; init; }
    }

    public sealed class MoveComponentStats
    {
        public required int N { get; init; }
        public required int Correct { get; init; }
        public required double Accuracy { get; init; }
        public required int TrueMove { get; init; }
        public required int TrueFlat { get; init; }
        public required int PredMove { get; init; }
        public required int PredFlat { get; init; }
    }

    public sealed class DirComponentStats
    {
        public required int N { get; init; }
        public required int Correct { get; init; }
        public required double Accuracy { get; init; }
        public required int MovePredTrue { get; init; }
        public required int MoveTrueButTruthFlat { get; init; }
    }

    public sealed class MicroComponentStats
    {
        public required int N { get; init; }
        public required int Correct { get; init; }
        public required double Accuracy { get; init; }
        public required int FactMicroDays { get; init; }
        public required int PredMicroDays { get; init; }
        public required double Coverage { get; init; }
    }
}
