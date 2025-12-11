using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	public static class Windowing
		{
		/// <summary>
		/// Единый таймзон Нью-Йорка для всех расчётов окон (таргеты, PnL, delayed).
		/// Алиас к TimeZones.NewYork, чтобы был один источник правды.
		/// </summary>
		public static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Основной удобный API: вычисляет момент базового выхода для дневной сделки
		/// в фиксированной NY-таймзоне.
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc )
			{
			return ComputeBaselineExitUtc (entryUtc, NyTz);
			}

		/// <summary>
		/// Низкоуровневая версия с явной таймзоной.
		/// Используется, если нужно посчитать exit относительно другой tz
		/// или в тестах/экспериментах.
		/// Вычисляет:
		/// - следующую рабочую NY-утреннюю границу 08:00 локального времени;
		/// - с небольшим смещением на 2 минуты назад.
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz )
			{
			var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);
			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				throw new InvalidOperationException (
					$"Baseline exit is not defined for weekend entry: {entryUtc:O}");

			DateTime exitDateLocal;

			if (ny.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday)
				{
				// Переход на следующее утро в рабочий день.
				exitDateLocal = ny.Date.AddDays (1);
				}
			else
				{
				// Для пятницы ищем следующее утро в первый рабочий день после уикенда.
				exitDateLocal = ny.Date.AddDays (1);
				while (exitDateLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					{
					exitDateLocal = exitDateLocal.AddDays (1);
					}
				}

			// 08:00 локального NY-времени (DST учитывается при конвертации в UTC).
			var exitLocal = new DateTime (
				exitDateLocal.Year,
				exitDateLocal.Month,
				exitDateLocal.Day,
				8, 0, 0,
				DateTimeKind.Unspecified);

			var exitUtc = TimeZoneInfo.ConvertTimeToUtc (exitLocal, nyTz);

			// Смещение на 2 минуты назад для визуального разделения открытия/закрытия.
			return exitUtc.AddMinutes (-2);
			}

		/// <summary>
		/// Фильтрует 6h-свечи, оставляя только окна, которые попадают
		/// в обучающие NY-окна (утро/день) в рабочие дни.
		/// </summary>
		public static List<Candle6h> FilterNyTrainWindows ( List<Candle6h> all, TimeZoneInfo nyTz )
			{
			var res = new List<Candle6h> ();

			foreach (var c in all)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (c.OpenTimeUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				bool isDst = nyTz.IsDaylightSavingTime (ny);

				if (isDst)
					{
					if (ny.Hour == 8 || ny.Hour == 14)
						res.Add (c);
					}
				else
					{
					if (ny.Hour == 7 || ny.Hour == 13)
						res.Add (c);
					}
				}

			return res.OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		/// <summary>
		/// Фильтрует 6h-свечи, оставляя только утренние NY-окна
		/// в рабочие дни (по текущей логике 7/8 часов в зависимости от DST).
		/// </summary>
		public static List<Candle6h> FilterNyMorningOnly ( List<Candle6h> all, TimeZoneInfo nyTz )
			{
			var res = new List<Candle6h> ();

			foreach (var c in all)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (c.OpenTimeUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				bool isDst = nyTz.IsDaylightSavingTime (ny);

				if (isDst)
					{
					if (ny.Hour == 8)
						res.Add (c);
					}
				else
					{
					if (ny.Hour == 7)
						res.Add (c);
					}
				}

			return res.OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		/// <summary>
		/// Проверяет, является ли момент utc утренним NY-окном
		/// (будний день и 7/8 часов в зависимости от DST).
		/// </summary>
		public static bool IsNyMorning ( DateTime utc, TimeZoneInfo nyTz )
			{
			var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, nyTz);
			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				return false;

			bool isDst = nyTz.IsDaylightSavingTime (ny);
			return isDst ? ny.Hour == 8 : ny.Hour == 7;
			}

		/// <summary>
		/// Строит spaced-test: берёт несколько блоков из конца ряда
		/// с пропусками между блоками.
		/// </summary>
		public static List<DataRow> BuildSpacedTest ( List<DataRow> rows, int take, int skip, int blocks )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (take <= 0 || blocks <= 0)
				return new List<DataRow> ();

			var n = rows.Count;
			if (n == 0)
				return new List<DataRow> ();

			// Верхняя оценка размера: take * blocks, но не больше n.
			var res = new List<DataRow> (Math.Min (n, take * blocks));

			// endExclusive — правая граница диапазона [start; endExclusive) (не включительно).
			var endExclusive = n;

			for (int b = 0; b < blocks && endExclusive > 0; b++)
				{
				var start = endExclusive - take;
				if (start < 0)
					start = 0;

				// Вместо Skip/Take работаем по индексам — O(K), где K — размер блока.
				for (int i = start; i < endExclusive; i++)
					res.Add (rows[i]);

				// Смещаем окно на предыдущий блок с пропуском skip элементов.
				endExclusive = start - skip;
				}

			// Итоговый список сортируем по дате, как и раньше.
			return res.OrderBy (r => r.Date).ToList ();
			}

		/// <summary>
		/// Группирует строки по блокам фиксированного размера (по дате).
		/// </summary>
		public static IEnumerable<List<DataRow>> GroupByBlocks ( List<DataRow> rows, int blockSize )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (blockSize <= 0) throw new ArgumentOutOfRangeException (nameof (blockSize));

			var sorted = rows.OrderBy (r => r.Date).ToList ();

			var cur = new List<DataRow> ();
			foreach (var r in sorted)
				{
				cur.Add (r);
				if (cur.Count == blockSize)
					{
					yield return cur;
					cur = new List<DataRow> ();
					}
				}

			if (cur.Count > 0)
				yield return cur;
			}
		}
	}
