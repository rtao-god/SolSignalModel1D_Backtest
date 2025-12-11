using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
	{
	/// <summary>
	/// Тренер дневного двухшагового пайплайна:
	/// 1) бинарная модель "есть ли ход" (move);
	/// 2) бинарные модели направления (dir-normal / dir-down);
	/// 3) микро-модель для боковика (через MicroFlatTrainer).
	/// Подготовка данных вынесена в DailyDatasetBuilder / DailyTrainingDataBuilder.
	/// </summary>
	public sealed class ModelTrainer
		{
		// === DEBUG-флаги для точечного отключения моделей ===
		// По умолчанию все false → поведение полностью совпадает с прежним.

		/// <summary>
		/// Если true, move-модель ("есть ли ход") не обучается и в бандле будет null.
		/// Удобно для абляционных тестов/поиска утечек.
		/// </summary>
		public bool DisableMoveModel { get; set; }

		/// <summary>
		/// Если true, модель направления для NORMAL-режима не обучается (DirModelNormal = null).
		/// </summary>
		public bool DisableDirNormalModel { get; set; }

		/// <summary>
		/// Если true, модель направления для DOWN-режима не обучается (DirModelDown = null).
		/// </summary>
		public bool DisableDirDownModel { get; set; }

		/// <summary>
		/// Если true, микро-модель боковика не обучается (MicroFlatModel = null).
		/// </summary>
		public bool DisableMicroFlatModel { get; set; }

		// Число потоков для LightGBM.
		private readonly int _gbmThreads = Math.Max (1, Environment.ProcessorCount - 1);

		// Общий MLContext для всех дневных/микро-моделей.
		private readonly MLContext _ml = new MLContext (seed: 42);

		// Граница "актуального" рынка для логов/метрик внутри тренера.
		private static readonly DateTime RecentCutoff = new DateTime (2025, 1, 1);

		// Балансировка классов (оставляем как было).
		private static readonly bool BalanceMove = false;
		private static readonly bool BalanceDir = true;
		private const double BalanceTargetFrac = 0.70;

		public ModelBundle TrainAll (
			List<DataRow> trainRows,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (trainRows == null) throw new ArgumentNullException (nameof (trainRows));

			if (trainRows.Count == 0)
				throw new InvalidOperationException ("TrainAll: empty trainRows — нечего обучать.");

			// === 1. Единый dataset-builder для дневной модели ===
			var trainUntil = trainRows.Max (r => r.Date);

			var dataset = DailyDatasetBuilder.Build (
				allRows: trainRows,
				trainUntil: trainUntil,
				balanceMove: BalanceMove,
				balanceDir: BalanceDir,
				balanceTargetFrac: BalanceTargetFrac,
				datesToExclude: datesToExclude);

			var moveTrainRows = dataset.MoveTrainRows;
			var dirNormalRows = dataset.DirNormalRows;
			var dirDownRows = dataset.DirDownRows;

			// ===== 1. Модель "есть ли ход" (move) =====
			// Цель: Label != 1 (non-flat по path-based label).
			ITransformer? moveModel = null;

			if (DisableMoveModel)
				{
				Console.WriteLine ("[2stage] move-model DISABLED by flag, skipped training");
				}
			else
				{
				if (moveTrainRows.Count == 0)
					{
					Console.WriteLine ("[2stage] move-model: train rows = 0, skipping");
					}
				else
					{
					var moveData = _ml.Data.LoadFromEnumerable (
						moveTrainRows.Select (r => new MlSampleBinary
							{
							Label = r.Label != 1,
							Features = MlTrainingUtils.ToFloatFixed (r.Features)
							}));

					var movePipe = _ml.BinaryClassification.Trainers.LightGbm (
						new LightGbmBinaryTrainer.Options
							{
							NumberOfLeaves = 16,
							NumberOfIterations = 90,
							LearningRate = 0.07f,
							MinimumExampleCountPerLeaf = 20,
							Seed = 42,
							NumberOfThreads = _gbmThreads
							});

					moveModel = movePipe.Fit (moveData);
					Console.WriteLine ($"[2stage] move-model trained on {moveTrainRows.Count} rows");
					}
				}

			// ===== 2. Направление (dir-normal / dir-down) =====

			ITransformer? dirNormalModel = null;
			ITransformer? dirDownModel = null;

			if (DisableDirNormalModel)
				{
				Console.WriteLine ("[2stage] dir-normal DISABLED by flag, skipped training");
				}
			else
				{
				dirNormalModel = BuildDirModel (dirNormalRows, "dir-normal");
				}

			if (DisableDirDownModel)
				{
				Console.WriteLine ("[2stage] dir-down DISABLED by flag, skipped training");
				}
			else
				{
				dirDownModel = BuildDirModel (dirDownRows, "dir-down");
				}

			// ===== 3. Микро-модель для боковика =====
			ITransformer? microModel = null;

			if (DisableMicroFlatModel)
				{
				Console.WriteLine ("[2stage] micro-flat DISABLED by flag, skipped training");
				}
			else
				{
				microModel = MicroFlatTrainer.BuildMicroFlatModel (_ml, dataset.TrainRows);
				}

			// Бандл остаётся тем же — чтобы не ломать внешний код.
			return new ModelBundle
				{
				MoveModel = moveModel,
				DirModelNormal = dirNormalModel,
				DirModelDown = dirDownModel,
				MicroFlatModel = microModel,
				MlCtx = _ml
				};
			}

		/// <summary>
		/// Тренировка бинарной модели направления для заданного режима.
		/// Цель: up? по path-based Label (2 = up, 0 = down).
		/// </summary>
		private ITransformer? BuildDirModel ( List<DataRow> rows, string tag )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			// rows сюда уже приходят только с Label ∈ {0,2} (см. DailyTrainingDataBuilder).
			if (rows.Count < 40)
				{
				Console.WriteLine ($"[2stage] {tag}: мало строк ({rows.Count}), скипаем");
				return null;
				}

			int recent = rows.Count (r => r.Date >= RecentCutoff);

			var data = _ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					// true = up (Label=2), false = down (Label=0)
					Label = r.Label == 2,
					Features = MlTrainingUtils.ToFloatFixed (r.Features)
					}));

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 16,
					NumberOfIterations = 90,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15,
					Seed = 42,
					NumberOfThreads = _gbmThreads
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage] {tag}: trained on {rows.Count} rows (recent {recent})");
			return model;
			}
		}
	}
