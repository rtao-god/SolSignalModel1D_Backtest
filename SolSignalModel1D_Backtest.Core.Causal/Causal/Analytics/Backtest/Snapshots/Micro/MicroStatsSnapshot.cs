using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro
{
	public sealed class MicroStatsSnapshot
		{
		public required FlatOnlyMicroBlock FlatOnly { get; init; }
		public required NonFlatDirectionBlock NonFlatDirection { get; init; }
		}

	public sealed class FlatOnlyMicroBlock
		{
		public required int MicroUpPred { get; init; }
		public required int MicroUpHit { get; init; }
		public required int MicroUpMiss { get; init; }

		public required int MicroDownPred { get; init; }
		public required int MicroDownHit { get; init; }
		public required int MicroDownMiss { get; init; }

		public required int MicroNonePredicted { get; init; }

		public required int TotalDirPred { get; init; }
		public required int TotalDirHit { get; init; }

		public required double AccUpPct { get; init; }
		public required double AccDownPct { get; init; }
		public required double AccAllPct { get; init; }
		}

	public sealed class NonFlatDirectionBlock
		{
		public required int Total { get; init; }
		public required int Correct { get; init; }

		public required int PredUp_FactUp { get; init; }
		public required int PredUp_FactDown { get; init; }
		public required int PredDown_FactDown { get; init; }
		public required int PredDown_FactUp { get; init; }

		public required double AccuracyPct { get; init; }
		}
	}
