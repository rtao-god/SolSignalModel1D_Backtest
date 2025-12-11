using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
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
	/// - дополнительно выбрасывает строки, чей baseline-exit за trainUntil;
	/// - опционально выкидывает datesToExclude;
	/// - делегирует разбиение на move/dir в DailyTrainingDataBuilder.
	///
	/// Здесь нет ML.NET — только DataRow.
	/// </summary>
	public static class DailyDatasetBuilder
		{
		// Используем тот же таймзон, что и RowBuilder/Windowing.
		private static readonly TimeZoneInfo NyTz = Windowing.NyTz;

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

			// 2. Базовый train-период по дате входа.
			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			// 3. Явные исключения дат (например, OOS).
			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				trainRows = trainRows
					.Where (r => !datesToExclude.Contains (r.Date))
					.ToList ();
				}

			// 4. Фильтрация по baseline-exit:
			//    оставляем только те строки, у которых baseline-exit не залезает за trainUntil.
			trainRows = FilterByBaselineExit (trainRows, trainUntil);

			// 5. Разбиение на move/dir-датасеты.
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

		/// <summary>
		/// Выбрасывает строки, baseline-exit которых позже trainUntil.
		/// Для нерабочих дней (weekend) baseline-exit по определению не задан,
		/// такие строки просто не попадают в train-набор (как и в RowBuilder).
		/// </summary>
		private static List<DataRow> FilterByBaselineExit (
			List<DataRow> rows,
			DateTime trainUntil )
			{
			var result = new List<DataRow> (rows.Count);

			foreach (var r in rows)
				{
				// В прод-пайплайне RowBuilder вообще не создаёт строк для выходных.
				// Здесь явно повторяем тот же контракт: weekend-строки не участвуют в train-наборе.
				var ny = TimeZoneInfo.ConvertTimeFromUtc (r.Date, NyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				// Если для буднего дня ComputeBaselineExitUtc кидает ошибку — это уже
				// реальная проблема входных данных, и её важно увидеть.
				var exitUtc = Windowing.ComputeBaselineExitUtc (r.Date, NyTz);

				if (exitUtc <= trainUntil)
					result.Add (r);
				// если exitUtc > trainUntil — это holdout, строку не включаем в train
				}

			return result;
			}
		}
	}
