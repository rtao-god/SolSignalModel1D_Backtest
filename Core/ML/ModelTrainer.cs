using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// 2-ступенчатый тренер + микро для реальных микро-дней (FactMicroUp/Down)
	/// </summary>
	public sealed class ModelTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);
		private static readonly DateTime RecentCutoff = new DateTime (2025, 1, 1);

		public ModelBundle TrainAll (
			List<DataRow> rows,
			HashSet<DateTime> testDates )
			{
			// train = всё, что НЕ в тесте
			var trainRows = rows
				.Where (r => !testDates.Contains (r.Date))
				.OrderByDescending (r => r.Date)
				.ToList ();

			// ===== 1) Модель "будет ход" =====
			var moveData = _ml.Data.LoadFromEnumerable (
				trainRows.Select (r => new MlSampleBinary
					{
					Label = Math.Abs (r.SolFwd1) >= r.MinMove,
					Features = r.Features.Select (f => (float) f).ToArray ()
					})
			);

			var movePipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 16,
					NumberOfIterations = 90,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 20
					});

			var moveModel = movePipe.Fit (moveData);
			Console.WriteLine ($"[2stage] move-model trained on {trainRows.Count} rows");

			// ===== 2) Строки с реальным ходом → модели направления =====
			var moveRows = trainRows.Where (r => Math.Abs (r.SolFwd1) >= r.MinMove).ToList ();

			var dirNormalRows = moveRows.Where (r => !r.RegimeDown).ToList ();
			var dirDownRows = moveRows.Where (r => r.RegimeDown).ToList ();

			var dirNormalModel = BuildDirModel (dirNormalRows, "dir-normal");
			var dirDownModel = BuildDirModel (dirDownRows, "dir-down");

			// ===== 3) Микро — только по твоим реальным микро-дням =====
			var microFlatModel = BuildMicroFlatModel (trainRows);

			return new ModelBundle
				{
				MoveModel = moveModel,
				DirModelNormal = dirNormalModel,
				DirModelDown = dirDownModel,
				MicroFlatModel = microFlatModel,
				MlCtx = _ml
				};
			}

		private ITransformer? BuildDirModel ( List<DataRow> rows, string tag )
			{
			if (rows.Count < 40)
				{
				Console.WriteLine ($"[2stage] {tag}: мало строк ({rows.Count}), скипаем");
				return null;
				}

			int recent = rows.Count (r => r.Date >= RecentCutoff);

			var data = _ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.SolFwd1 > 0,
					Features = r.Features.Select (f => (float) f).ToArray ()
					})
			);

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 16,
					NumberOfIterations = 90,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage] {tag}: trained on {rows.Count} rows (recent {recent})");
			return model;
			}

		/// <summary>
		/// микро для боковика — только реальные микро-дни из RowBuilder (FactMicroUp/Down)
		/// </summary>
		private ITransformer? BuildMicroFlatModel ( List<DataRow> rows )
			{
			// берём только то, что ты уже пометил как микро
			var flats = rows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderByDescending (r => r.Date)
				.ToList ();

			if (flats.Count < 30)
				{
				Console.WriteLine ("[2stage-micro] мало микро-дней, скипаем");
				return null;
				}

			// можно чуть сбалансировать, но без фанатизма
			var upFlats = flats.Where (r => r.FactMicroUp).ToList ();
			var downFlats = flats.Where (r => r.FactMicroDown).ToList ();
			int take = Math.Min (upFlats.Count, downFlats.Count);
			if (take > 0)
				{
				upFlats = upFlats.Take (take).ToList ();
				downFlats = downFlats.Take (take).ToList ();
				flats = upFlats.Concat (downFlats)
							   .OrderByDescending (r => r.Date)
							   .ToList ();
				}

			var data = _ml.Data.LoadFromEnumerable (
				flats.Select (r => new MlSampleBinary
					{
					Label = r.FactMicroUp, // вверх = 1
					Features = r.Features.Select (f => (float) f).ToArray ()
					})
			);

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 12,
					NumberOfIterations = 70,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage-micro] обучено на {flats.Count} REAL микро-днях");
			return model;
			}
		}
	}
