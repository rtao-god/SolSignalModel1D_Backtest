using System;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Сэмпл для сильного отката (Model A).
	/// Label = true → "отложка реально спасла плохой день".
	/// </summary>
	public sealed class PullbackContinuationSample
		{
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public bool Label { get; set; }

		/// <summary>Нужен для каузального среза.</summary>
		public DateTime EntryUtc { get; set; }

		/// <summary>
		/// Связь с исходной записью бэктеста (не попадает в ML.NET schema).
		/// Нужна тренерам/диагностике, которые ожидают .Causal/.Forward.
		/// </summary>
		[NoColumn] public required BacktestRecord Record { get; init; }
		[NoColumn] public ForwardOutcomes Forward => Record.Forward;
		[NoColumn] public CausalPredictionRecord Causal => Record.Causal;
		}
	}