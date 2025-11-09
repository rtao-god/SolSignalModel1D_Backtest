using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// 2-ступенчатый тренер + микро, детерминизированный.
	/// Сюда надо давать ТОЛЬКО train-строки текущего ролла (без будущего).
	/// </summary>
	public sealed class ModelTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);
		private static readonly DateTime RecentCutoff = new DateTime (2025, 1, 1);

		// ===== настройки балансировки =====
		// move-модель чаще и так работает нормально → по умолчанию не трогаем
		private const bool BalanceMove = false;

		// а вот направление хотим подкормить
		private const bool BalanceDir = true;

		// до какой доли от мажорного класса дотягиваем минорный
		// (1.0 = полностью выровнять, 0.7 = умеренно)
		private const double BalanceTargetFrac = 0.70;

		public ModelBundle TrainAll (
			List<DataRow> trainRows,
			HashSet<DateTime>? datesToExclude = null )
			{
			// выкидываем, что просили (обычно: тестовые даты этого окна)
			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				trainRows = trainRows
					.Where (r => !datesToExclude.Contains (r.Date))
					.ToList ();
				}

			// фиксируем порядок
			trainRows = trainRows
				.OrderBy (r => r.Date)
				.ToList ();

			// ===== 1) модель "будет ход" =====
			// при желании можно тоже сбалансировать
			List<DataRow> moveTrainRows = trainRows;
			if (BalanceMove)
				{
				moveTrainRows = OversampleBinary (
					trainRows,
					r => Math.Abs (r.SolFwd1) >= r.MinMove,   // "есть ход" = 1
					BalanceTargetFrac
				);
				}

			var moveData = _ml.Data.LoadFromEnumerable (
				moveTrainRows.Select (r => new MlSampleBinary
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
					MinimumExampleCountPerLeaf = 20,
					Seed = 42,
					NumberOfThreads = 1
					});

			var moveModel = movePipe.Fit (moveData);
			Console.WriteLine ($"[2stage] move-model trained on {moveTrainRows.Count} rows");

			// ===== 2) строки с реальным ходом → модели направления =====
			var moveRows = trainRows
				.Where (r => Math.Abs (r.SolFwd1) >= r.MinMove)
				.OrderBy (r => r.Date)
				.ToList ();

			var dirNormalRows = moveRows.Where (r => !r.RegimeDown).OrderBy (r => r.Date).ToList ();
			var dirDownRows = moveRows.Where (r => r.RegimeDown).OrderBy (r => r.Date).ToList ();

			if (BalanceDir)
				{
				dirNormalRows = OversampleBinary (
					dirNormalRows,
					r => r.SolFwd1 > 0,
					BalanceTargetFrac
				);
				dirDownRows = OversampleBinary (
					dirDownRows,
					r => r.SolFwd1 > 0,
					BalanceTargetFrac
				);
				}

			var dirNormalModel = BuildDirModel (dirNormalRows, "dir-normal");
			var dirDownModel = BuildDirModel (dirDownRows, "dir-down");

			// ===== 3) микро =====
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
					MinimumExampleCountPerLeaf = 15,
					Seed = 42,
					NumberOfThreads = 1
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage] {tag}: trained on {rows.Count} rows (recent {recent})");
			return model;
			}

		/// <summary>
		/// микро для боковика — только реальные микро-дни из RowBuilder
		/// </summary>
		private ITransformer? BuildMicroFlatModel ( List<DataRow> rows )
			{
			var flats = rows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderBy (r => r.Date)
				.ToList ();

			if (flats.Count < 30)
				{
				Console.WriteLine ("[2stage-micro] мало микро-дней, скипаем");
				return null;
				}

			var upFlats = flats.Where (r => r.FactMicroUp).ToList ();
			var downFlats = flats.Where (r => r.FactMicroDown).ToList ();
			int take = Math.Min (upFlats.Count, downFlats.Count);
			if (take > 0)
				{
				upFlats = upFlats.Take (take).OrderBy (r => r.Date).ToList ();
				downFlats = downFlats.Take (take).OrderBy (r => r.Date).ToList ();
				flats = upFlats.Concat (downFlats)
							   .OrderBy (r => r.Date)
							   .ToList ();
				}

			var data = _ml.Data.LoadFromEnumerable (
				flats.Select (r => new MlSampleBinary
					{
					Label = r.FactMicroUp,
					Features = r.Features.Select (f => (float) f).ToArray ()
					})
			);

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 12,
					NumberOfIterations = 70,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15,
					Seed = 42,
					NumberOfThreads = 1
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage-micro] обучено на {flats.Count} REAL микро-днях");
			return model;
			}

		/// <summary>
		/// Умеренный oversampling бинарного набора:
		/// дотягиваем меньший класс до targetFrac * большего.
		/// Порядок потом фиксируем по дате.
		/// </summary>
		private static List<DataRow> OversampleBinary (
			List<DataRow> src,
			Func<DataRow, bool> isPositive,
			double targetFrac )
			{
			var pos = src.Where (isPositive).ToList ();
			var neg = src.Where (r => !isPositive (r)).ToList ();

			int posCount = pos.Count;
			int negCount = neg.Count;

			if (posCount == 0 || negCount == 0)
				return src;

			bool posIsMajor = posCount >= negCount;
			int majorCount = posIsMajor ? posCount : negCount;
			int minorCount = posIsMajor ? negCount : posCount;

			int target = (int) Math.Round (majorCount * targetFrac, MidpointRounding.AwayFromZero);
			if (target <= minorCount)
				{
				// и так ок
				return src;
				}

			// кого дублируем
			var minorList = posIsMajor ? neg : pos;
			var result = new List<DataRow> (src.Count + (target - minorCount));
			result.AddRange (src);

			int need = target - minorCount;
			for (int i = 0; i < need; i++)
				{
				// детерминизм: просто крутимся по списку
				var clone = minorList[i % minorList.Count];
				result.Add (clone);
				}

			// важно: вернуть в одном порядке по дате
			return result.OrderBy (r => r.Date).ToList ();
			}
		}
	}
