using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	public sealed class MlSampleBinary
		{
		public bool Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
		}
	}
