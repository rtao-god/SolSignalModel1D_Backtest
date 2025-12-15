using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
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

		// ---- инфраструктурные поля для тренеров/диагностики (не участвуют в ML.NET schema) ----

		[NoColumn]
		public BacktestRecord? Record { get; set; }

		[NoColumn]
		public CausalPredictionRecord Causal =>
			Record?.Causal ?? throw new InvalidOperationException ("[ml] SlHitSample.Record is null");

		[NoColumn]
		public ForwardOutcomes Forward =>
			Record?.Forward ?? throw new InvalidOperationException ("[ml] SlHitSample.Record is null");
		}

	public sealed class SlHitPrediction
		{
		[ColumnName ("PredictedLabel")]
		public bool PredictedLabel { get; set; }

		[ColumnName ("Score")]
		public float Score { get; set; }

		[ColumnName ("Probability")]
		public float Probability { get; set; }
		}
	}
