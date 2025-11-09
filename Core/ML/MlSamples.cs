using System;
using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	// Базовый бинарный — для основной модели (move/dir/micro)
	public sealed class MlSampleBinary
		{
		public bool Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
		}

	public sealed class MlBinaryOutput
		{
		public bool PredictedLabel { get; set; }
		public float Score { get; set; }
		public float Probability { get; set; }
		}

	// ===== SL-модель (will hit SL first?) =====
	public sealed class SlHitSample
		{
		/// <summary>true = сначала был SL, false = сначала был TP</summary>
		public bool Label { get; set; }

		/// <summary>фичи на момент входа (1h/6h локальные)</summary>
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		/// <summary>дата входа — чтобы считать вес по давности</summary>
		public DateTime EntryUtc { get; set; }

		/// <summary>можно задать вручную, но обычно мы считаем в тренере</summary>
		public float Weight { get; set; }
		}

	public sealed class SlHitPrediction
		{
		public bool PredictedLabel { get; set; }
		public float Score { get; set; }
		public float Probability { get; set; }
		}
	}
