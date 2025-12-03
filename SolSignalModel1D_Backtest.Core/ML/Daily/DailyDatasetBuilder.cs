using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Core.ML.Daily
	{
	/// <summary>
	/// DTO для train-датасета дневной модели:
	/// - TrainRows: все строки train-периода после фильтров;
	/// - MoveTrainRows / DirNormalRows / DirDownRows — выборки под конкретные задачи.
	/// </summary>
	public sealed class DailyDataset
		{
		public List<DataRow> TrainRows { get; }
		public List<DataRow> MoveTrainRows { get; }
		public List<DataRow> DirNormalRows { get; }
		public List<DataRow> DirDownRows { get; }
		public DateTime TrainUntilUtc { get; }

		public DailyDataset (
			List<DataRow> trainRows,
			List<DataRow> moveTrainRows,
			List<DataRow> dirNormalRows,
			List<DataRow> dirDownRows,
			DateTime trainUntilUtc )
			{
			TrainRows = trainRows ?? throw new ArgumentNullException (nameof (trainRows));
			MoveTrainRows = moveTrainRows ?? throw new ArgumentNullException (nameof (moveTrainRows));
			DirNormalRows = dirNormalRows ?? throw new ArgumentNullException (nameof (dirNormalRows));
			DirDownRows = dirDownRows ?? throw new ArgumentNullException (nameof (dirDownRows));
			TrainUntilUtc = trainUntilUtc;
			}
		}

	/// <summary>
	/// Единая точка сборки дневного датасета:
	/// - режет по trainUntil (r.Date <= trainUntil);
	/// - опционально выкидывает datesToExclude;
	/// - делегирует разбиение на move/dir в DailyTrainingDataBuilder.
	///
	/// Здесь нет ML.NET — только DataRow.
	/// </summary>
	public static class DailyDatasetBuilder
		{
		public static DailyDataset Build (
			List<DataRow> allRows,
			DateTime trainUntil,
			bool balanceMove,
			bool balanceDir,
			double balanceTargetFrac,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			// 1. Каузальная сортировка по дате.
			var ordered = allRows
				.OrderBy (r => r.Date)
				.ToList ();

			// 2. Train-период по дате.
			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			// 3. Исключаем явно заданные даты (например, OOS).
			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				trainRows = trainRows
					.Where (r => !datesToExclude.Contains (r.Date))
					.ToList ();
				}

			// 4. Разбиение на move/dir-датасеты.
			DailyTrainingDataBuilder.Build (
				trainRows: trainRows,
				balanceMove: balanceMove,
				balanceDir: balanceDir,
				balanceTargetFrac: balanceTargetFrac,
				moveTrainRows: out var moveTrainRows,
				dirNormalRows: out var dirNormalRows,
				dirDownRows: out var dirDownRows);

			return new DailyDataset (
				trainRows: trainRows,
				moveTrainRows: moveTrainRows,
				dirNormalRows: dirNormalRows,
				dirDownRows: dirDownRows,
				trainUntilUtc: trainUntil);
			}
		}
	}
