using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed
	{
	/// <summary>
	/// Оффлайн-сэмпл и заодно входной тип для онлайн-предсказаний target-level.
	/// </summary>
	public sealed class TargetLevelSample
		{
		/// <summary>
		/// 0 / 1 / 2 — класс глубины отката.
		/// В онлайне при предсказании можно ставить любое (оно не используется).
		/// </summary>
		public int Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		/// <summary>
		/// время входа — нужно только для каузального обучения, в онлайне можно не трогать
		/// </summary>
		public DateTime EntryUtc { get; set; }
		}
	}