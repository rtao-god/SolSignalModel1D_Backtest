using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public static class Windowing
		{
		/// <summary>
		/// Единый таймзон Нью-Йорка для всех расчётов окон (таргеты, PnL, delayed).
		/// Ищет как IANA ("America/New_York"), так и Windows ("Eastern Standard Time").
		/// Если не найдено — бросает ошибку, чтобы не молча свалиться в UTC.
		/// </summary>
		public static readonly TimeZoneInfo NyTz = ResolveNyTimeZone ();

		private static TimeZoneInfo ResolveNyTimeZone ()
			{
			foreach (var tz in TimeZoneInfo.GetSystemTimeZones ())
				{
				if (tz.Id == "America/New_York" || tz.Id == "Eastern Standard Time")
					return tz;
				}
			throw new InvalidOperationException (
				"Cannot resolve New York timezone for Windowing.NyTz. " +
				"Adjust resolver (America/New_York / Eastern Standard Time) for target platform.");
			}

		/// <summary>
		/// Удобная обёртка: baseline-выход при фиксированном NY таймзоне.
		/// Используется в PnL и delayed-логике, чтобы жить в том же мире, что и таргеты.
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc )
			{
			// Внутри вызываем старую версию, где таймзона передаётся явно.
			return ComputeBaselineExitUtc (entryUtc, NyTz);
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
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
				bool isDst = nyTz.IsDaylightSavingTime (ny);
				if (isDst)
					{
					if (ny.Hour == 8 || ny.Hour == 14) res.Add (c);
					}
				else
					{
					if (ny.Hour == 7 || ny.Hour == 13) res.Add (c);
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
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
				bool isDst = nyTz.IsDaylightSavingTime (ny);
				if (isDst)
					{
					if (ny.Hour == 8) res.Add (c);
					}
				else
					{
					if (ny.Hour == 7) res.Add (c);
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
			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
			bool isDst = nyTz.IsDaylightSavingTime (ny);
			return isDst ? ny.Hour == 8 : ny.Hour == 7;
			}

		/// <summary>
		/// Вычисляет момент базового выхода для дневной сделки:
		/// следующая рабочая NY-утренняя граница 08:00 локального времени,
		/// с небольшим смещением на 2 минуты назад.
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz )
			{
			var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);
			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				throw new InvalidOperationException (
					$"Baseline exit is not defined for weekend entry: {entryUtc:O}");

			DateTime exitDateLocal;

			if (ny.DayOfWeek is DayOfWeek.Monday
				or DayOfWeek.Tuesday
				or DayOfWeek.Wednesday
				or DayOfWeek.Thursday)
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
				exitDateLocal.Year, exitDateLocal.Month, exitDateLocal.Day,
				8, 0, 0, DateTimeKind.Unspecified);

			var exitUtc = TimeZoneInfo.ConvertTimeToUtc (exitLocal, nyTz);

			// Смещение на 2 минуты назад для визуального разделения открытия/закрытия.
			return exitUtc.AddMinutes (-2);
			}

		/// <summary>
		/// Строит spaced-test: берёт несколько блоков из конца ряда с пропусками между блоками.
		/// </summary>
		public static List<DataRow> BuildSpacedTest ( List<DataRow> rows, int take, int skip, int blocks )
			{
			var res = new List<DataRow> ();
			int n = rows.Count;
			int end = n;
			for (int b = 0; b < blocks; b++)
				{
				int start = end - take;
				if (start < 0) start = 0;
				var part = rows.Skip (start).Take (end - start).ToList ();
				res.AddRange (part);
				end = start - skip;
				if (end <= 0) break;
				}
			return res.OrderBy (r => r.Date).ToList ();
			}

		/// <summary>
		/// Группирует строки по блокам фиксированного размера.
		/// </summary>
		public static IEnumerable<List<DataRow>> GroupByBlocks ( List<DataRow> rows, int blockSize )
			{
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
			if (cur.Count > 0) yield return cur;
			}
		}
	}
