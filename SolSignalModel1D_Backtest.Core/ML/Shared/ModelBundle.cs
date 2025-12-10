using Microsoft.ML;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	/// <summary>
	/// Контейнер для всех моделей, которые реально используются рантаймом.
	/// Только то, что дергает PredictionEngine.
	/// </summary>
	public sealed class ModelBundle
		{
		// MLContext логически всегда не null:
		// он создаётся один раз в тренере и всегда прокидывается в бандл.
		// Инициализация через null! нужна только для успокоения анализатора
		// до момента, когда значение будет задано через init-сеттер.
		public MLContext MlCtx { get; init; } = null!;

		// 1. модель "будет ли ход вообще"
		public ITransformer? MoveModel { get; init; }

		// 2. направление в нормальном режиме
		public ITransformer? DirModelNormal { get; init; }

		// 3. направление в даун-режиме
		public ITransformer? DirModelDown { get; init; }

		// 4. микро-боковик (наклон)
		public ITransformer? MicroFlatModel { get; init; }
		}
	}
