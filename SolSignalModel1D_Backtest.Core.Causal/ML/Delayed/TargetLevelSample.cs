using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Delayed
	{
	public sealed class TargetLevelSample
		{
		public int Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public DateTime EntryUtc { get; set; }
		}
	}
