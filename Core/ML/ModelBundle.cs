using Microsoft.ML;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Контейнер для всех моделей, которые реально используются рантаймом.
	/// Никакого легаси, только то, что дергает PredictionEngine.
	/// </summary>
	public sealed class ModelBundle
		{
		public MLContext? MlCtx { get; init; }

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
