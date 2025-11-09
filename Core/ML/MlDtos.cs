using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	// обычный мультикласс
	public sealed class MlSample
		{
		public float Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
		}

	// мультикласс с весом
	public sealed class MlSampleWeighted
		{
		public float Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public float Weight { get; set; }
		}
	}
