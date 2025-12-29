using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro
{
	public sealed class MicroStatsSnapshot
		{
		public required FlatOnlyMicroBlock FlatOnly { get; init; }
		public required NonFlatDirectionBlock NonFlatDirection { get; init; }
		}

	public sealed class FlatOnlyMicroBlock
		{
		public required int TotalFactDays { get; init; }

		public required int MicroUpPred { get; init; }
		public required int MicroUpHit { get; init; }
		public required int MicroUpMiss { get; init; }

		public required int MicroDownPred { get; init; }
		public required int MicroDownHit { get; init; }
		public required int MicroDownMiss { get; init; }

		public required int MicroNonePredicted { get; init; }

		public required int TotalDirPred { get; init; }
		public required int TotalDirHit { get; init; }

		public required OptionalValue<double> CoveragePct { get; init; }

		public required OptionalValue<double> AccUpPct { get; init; }
		public required OptionalValue<double> AccDownPct { get; init; }
		public required OptionalValue<double> AccAllPct { get; init; }
		public required OptionalValue<double> AccAllWithNonePct { get; init; }
		}

	public sealed class NonFlatDirectionBlock
		{
		public required int Total { get; init; }
		public required int Correct { get; init; }

		public required int PredUp_FactUp { get; init; }
		public required int PredUp_FactDown { get; init; }
		public required int PredDown_FactDown { get; init; }
		public required int PredDown_FactUp { get; init; }

		public required OptionalValue<double> AccuracyPct { get; init; }
		}
	}
