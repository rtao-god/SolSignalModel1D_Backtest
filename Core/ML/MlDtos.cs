using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	// обычный мультикласс (если где-то ещё нужен)
	public sealed class MlSample
		{
		public float Label { get; set; }

		[VectorType (24)]
		public float[] Features { get; set; } = Array.Empty<float> ();
		}

	// МУЛЬТИКЛАСС С ВЕСОМ — это для LightGbmModelTrainer, пусть лежит
	public sealed class MlSampleWeighted
		{
		public float Label { get; set; }

		[VectorType (24)]
		public float[] Features { get; set; } = Array.Empty<float> ();

		public float Weight { get; set; }
		}

	// бинарка для микро и вообще для бинарных моделей
	public sealed class MlSampleBinary
		{
		public bool Label { get; set; }

		[VectorType (24)]
		public float[] Features { get; set; } = Array.Empty<float> ();
		}

	// выход мультикласса
	public sealed class MlOutput
		{
		public float PredictedLabel { get; set; }
		public float[] Score { get; set; } = Array.Empty<float> ();
		}

	// выход бинарки
	public sealed class MlBinaryOutput
		{
		public bool PredictedLabel { get; set; }
		public float Score { get; set; }
		public float Probability { get; set; }
		}
	}
