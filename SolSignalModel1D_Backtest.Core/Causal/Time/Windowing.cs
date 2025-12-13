using System;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
	{
	/// <summary>
	/// Каузальный time-contract для NY-окон:
	/// - единая NY таймзона (один источник правды);
	/// - вычисление baseline-exit (entryUtc -> следующее рабочее NY утро 08:00 - 2 минуты);
	/// - проверка "NY morning" для entry-точек.
	/// </summary>
	public static class Windowing
		{
		/// <summary>
		/// Единый таймзон Нью-Йорка для всех расчётов окон.
		/// Алиас к TimeZones.NewYork, чтобы был один источник правды.
		/// </summary>
		public static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Основной удобный API: baseline-exit для дневной сделки в фиксированной NY-таймзоне.
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc )
			{
			return ComputeBaselineExitUtc (entryUtc, NyTz);
			}

		/// <summary>
		/// Низкоуровневая версия с явной таймзоной.
		/// Вычисляет:
		/// - следующее рабочее NY-утро 08:00 локального времени;
		/// - минус 2 минуты (служебный оффсет).
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz )
			{
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);

			// Контракт: для weekend entry baseline-exit не определён.
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
				// Для пятницы: следующее утро в первый рабочий день после уикенда.
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
		/// Проверяет, является ли момент utc утренним NY-окном:
		/// будний день и 7/8 часов в зависимости от DST.
		/// </summary>
		public static bool IsNyMorning ( DateTime utc, TimeZoneInfo nyTz )
			{
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, nyTz);
			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				return false;

			bool isDst = nyTz.IsDaylightSavingTime (ny);
			return isDst ? ny.Hour == 8 : ny.Hour == 7;
			}
		}
	}
