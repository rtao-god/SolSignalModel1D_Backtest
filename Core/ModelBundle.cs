using Microsoft.ML;

namespace SolSignalModel1D_Backtest.Core
	{
	// это просто контейнер "всё, что мы натренили"
	public sealed class ModelBundle
		{
		// старые логистики — пусть живут
		public OvrLogistic NormalModel { get; init; } = null!;
		public OvrLogistic DownModel { get; init; } = null!;
		public BinaryLogistic? MicroModel { get; init; }
		public LinearReg? MoveRegressor { get; init; }

		// LGBM-одноступенчатый (то, что мы сейчас мучили)
		public LightGbmPredictor? LgbmPredictor { get; init; }

		// НОВОЕ: двухшаговая схема
		public ITransformer? MoveModel { get; init; }          // “будет ли ход”
		public ITransformer? DirModelDown { get; init; }       // направление в даун-режиме
		public ITransformer? DirModelNormal { get; init; }     // направление в нормальном режиме
		public MLContext? MlCtx { get; init; }                 // нужен, чтобы предсказывать
		public ITransformer? MicroFlatModel { get; init; }

		}
	}
