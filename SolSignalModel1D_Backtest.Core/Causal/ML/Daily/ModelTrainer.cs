using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Micro;
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

		// Важно: делаем UTC, т.к. DataRow.Date у тебя по контракту UTC.
		private static readonly DateTime RecentCutoff = new DateTime (2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static readonly bool BalanceMove = false;
		private static readonly bool BalanceDir = true;
		private const double BalanceTargetFrac = 0.70;

		public ModelBundle TrainAll (
			IReadOnlyList<DataRow> trainRows,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (trainRows == null) throw new ArgumentNullException (nameof (trainRows));
			if (trainRows.Count == 0)
				throw new InvalidOperationException ("TrainAll: empty trainRows — нечего обучать.");

			// ВАЖНО: trainUntil — в терминах baseline-exit.
			var trainUntilUtc = DeriveMaxBaselineExitUtc (trainRows, Windowing.NyTz);

			var dataset = DailyDatasetBuilder.Build (
				allRows: trainRows,
				trainUntilUtc: trainUntilUtc,
				balanceMove: BalanceMove,
				balanceDir: BalanceDir,
				balanceTargetFrac: BalanceTargetFrac,
				datesToExclude: datesToExclude);

			var moveTrainRows = dataset.MoveTrainRows;
			var dirNormalRows = dataset.DirNormalRows;
			var dirDownRows = dataset.DirDownRows;

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
				// ВАЖНО: MicroFlatTrainer должен принимать IReadOnlyList<DataRow>.
				// Если сейчас он ожидает List<DataRow>, это и даёт твою CS1503 на ModelTrainer.cs(118).
				microModel = MicroFlatTrainer.BuildMicroFlatModel (_ml, dataset.TrainRows);
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

		private ITransformer? BuildDirModel ( IReadOnlyList<DataRow> rows, string tag )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			if (rows.Count < 40)
				{
				Console.WriteLine ($"[2stage] {tag}: мало строк ({rows.Count}), скипаем");
				return null;
				}

			int recent = rows.Count (r => r.Date >= RecentCutoff);

			var data = _ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
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

		private static DateTime DeriveMaxBaselineExitUtc ( IReadOnlyList<DataRow> rows, TimeZoneInfo nyTz )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (rows.Count == 0) throw new ArgumentException ("rows must be non-empty.", nameof (rows));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			bool hasAny = false;
			DateTime maxExit = default;

			for (int i = 0; i < rows.Count; i++)
				{
				var entryUtc = rows[i].Date;

				if (entryUtc == default)
					throw new InvalidOperationException ("[2stage] DataRow.Date is default(DateTime).");
				if (entryUtc.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[2stage] DataRow.Date must be UTC, got Kind={entryUtc.Kind} for {entryUtc:O}.");

				var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

				if (!hasAny || exitUtc > maxExit)
					{
					maxExit = exitUtc;
					hasAny = true;
					}
				}

			if (!hasAny)
				throw new InvalidOperationException ("[2stage] failed to derive max baseline-exit: no working-day entries.");

			return maxExit;
			}
		}
	}
