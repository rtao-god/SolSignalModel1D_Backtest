using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Чистый ML-сэмпл для модели A (pullback continuation).
	/// Инвариант: тип содержит ТОЛЬКО поля, которые участвуют в ML.NET schema.
	/// Любые omniscient/diagnostics ссылки вынесены в отдельный контекстный DTO.
	/// </summary>
	public sealed class PullbackContinuationSample
		{
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public bool Label { get; set; }

		/// <summary>
		/// Каузальная метка времени сэмпла (нужна для среза train/predict).
		/// Должна быть UTC; иначе каузальный фильтр по asOf ломается.
		/// </summary>
		public DateTime EntryUtc { get; set; }
		}
	}
