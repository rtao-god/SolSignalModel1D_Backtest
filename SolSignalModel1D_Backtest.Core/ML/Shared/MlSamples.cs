using System;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

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
			// Явно мапим с выходами пайплайна
			[ColumnName ("PredictedLabel")]
			public bool PredictedLabel { get; set; }

			[ColumnName ("Score")]
			public float Score { get; set; }

			[ColumnName ("Probability")]
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
			// Полностью аналогично BinaryPrediction — явное связывание имён колонок
			[ColumnName ("PredictedLabel")]
			public bool PredictedLabel { get; set; }

			[ColumnName ("Score")]
			public float Score { get; set; }

			[ColumnName ("Probability")]
			public float Probability { get; set; }
		}
	}
