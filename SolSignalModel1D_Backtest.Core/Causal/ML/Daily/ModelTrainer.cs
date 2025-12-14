using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
	{
	public sealed class ModelTrainer
		{
		public bool DisableMoveModel { get; set; }
		public bool DisableDirNormalModel { get; set; }
		public bool DisableDirDownModel { get; set; }
		public bool DisableMicroFlatModel { get; set; }

		private readonly int _gbmThreads = Math.Max (1, Environment.ProcessorCount - 1);
		private readonly MLContext _ml = new MLContext (seed: 42);

		private static readonly bool BalanceMove = false;
		private static readonly bool BalanceDir = true;
		private const double BalanceTargetFrac = 0.70;

		public ModelBundle TrainAll (
			IReadOnlyList<LabeledCausalRow> trainRows,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (trainRows == null) throw new ArgumentNullException (nameof (trainRows));
			if (trainRows.Count == 0)
				throw new InvalidOperationException ("TrainAll: empty trainRows — нечего обучать.");

			var ordered = trainRows
				.OrderBy (r => r.DateUtc)
				.ToList ();

			if (datesToExclude != null && datesToExclude.Count > 0)
				ordered = ordered.Where (r => !datesToExclude.Contains (r.DateUtc)).ToList ();

			if (ordered.Count == 0)
				throw new InvalidOperationException ("TrainAll: all rows excluded by datesToExclude.");

			// Move: бинарная задача "движение vs flat"
			var moveRows = ordered;

			// Dir: только move-дни (не flat)
			var dirRows = ordered.Where (r => r.TrueLabel != 1).ToList ();

			// Разделение dir по режиму (если у тебя это реально используется)
			var dirNormalRows = dirRows.Where (r => !r.Causal.RegimeDown).ToList ();
			var dirDownRows = dirRows.Where (r => r.Causal.RegimeDown).ToList ();

			if (BalanceMove)
				{
				moveRows = MlTrainingUtils.OversampleBinary (
					src: moveRows,
					isPositive: r => r.TrueLabel != 1,
					dateSelector: r => r.DateUtc,
					targetFrac: BalanceTargetFrac);
				}

			if (BalanceDir)
				{
				dirNormalRows = MlTrainingUtils.OversampleBinary (
					src: dirNormalRows,
					isPositive: r => r.TrueLabel == 2,
					dateSelector: r => r.DateUtc,
					targetFrac: BalanceTargetFrac);

				dirDownRows = MlTrainingUtils.OversampleBinary (
					src: dirDownRows,
					isPositive: r => r.TrueLabel == 2,
					dateSelector: r => r.DateUtc,
					targetFrac: BalanceTargetFrac);
				}

			ITransformer? moveModel = null;

			if (DisableMoveModel)
				{
				Console.WriteLine ("[2stage] move-model DISABLED by flag, skipped training");
				}
			else
				{
				if (moveRows.Count == 0)
					{
					Console.WriteLine ("[2stage] move-model: train rows = 0, skipping");
					}
				else
					{
					var moveData = _ml.Data.LoadFromEnumerable (
						moveRows.Select (r => new MlSampleBinary
							{
							Label = r.TrueLabel != 1,
							Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
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
					Console.WriteLine ($"[2stage] move-model trained on {moveRows.Count} rows");
					}
				}

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

			ITransformer? microModel = null;

			if (DisableMicroFlatModel)
				{
				Console.WriteLine ("[2stage] micro-flat DISABLED by flag, skipped training");
				}
			else
				{
				// Micro тренируем ТОЛЬКО на flat-днях, где есть micro truth.
				// Если truth нет — day считается нейтральным (и в обучении micro не участвует).
				var microRows = ordered
					.Where (r => r.TrueLabel == 1 && (r.FactMicroUp || r.FactMicroDown))
					.ToList ();

				if (microRows.Count < 40)
					{
					Console.WriteLine ($"[2stage] micro-flat: too few rows ({microRows.Count}), skipping");
					microModel = null;
					}
				else
					{
					var microData = _ml.Data.LoadFromEnumerable (
						microRows.Select (r => new MlSampleBinary
							{
							Label = r.FactMicroUp, // true=up, false=down
							Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
							}));

					var microPipe = _ml.BinaryClassification.Trainers.LightGbm (
						new LightGbmBinaryTrainer.Options
							{
							NumberOfLeaves = 16,
							NumberOfIterations = 90,
							LearningRate = 0.07f,
							MinimumExampleCountPerLeaf = 15,
							Seed = 42,
							NumberOfThreads = _gbmThreads
							});

					microModel = microPipe.Fit (microData);
					Console.WriteLine ($"[2stage] micro-flat trained on {microRows.Count} rows");
					}
				}

			return new ModelBundle
				{
				MoveModel = moveModel,
				DirModelNormal = dirNormalModel,
				DirModelDown = dirDownModel,
				MicroFlatModel = microModel,
				MlCtx = _ml
				};
			}

		private ITransformer? BuildDirModel ( IReadOnlyList<LabeledCausalRow> rows, string tag )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			if (rows.Count < 40)
				{
				Console.WriteLine ($"[2stage] {tag}: мало строк ({rows.Count}), скипаем");
				return null;
				}

			var data = _ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.TrueLabel == 2,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
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
			Console.WriteLine ($"[2stage] {tag}: trained on {rows.Count} rows");
			return model;
			}
		}
	}
