using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	public sealed class SmallImprovementSample
		{
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public bool Label { get; set; }

		public DateTime EntryUtc { get; set; }
		}
	}
