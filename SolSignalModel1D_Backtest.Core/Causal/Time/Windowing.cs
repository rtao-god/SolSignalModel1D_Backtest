using System;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
	{
	/// <summary>
	/// Каузальный time-contract для NY-окон.
	/// Инварианты:
	/// - входы всегда UTC;
	/// - weekend-entry запрещён (baseline-exit не определён);
	/// - "NY morning" зависит от DST: 07:00 зимой / 08:00 летом;
	/// - baseline-exit = следующее NY-утро (07/08) минус 2 минуты (06:58/07:58).
	/// </summary>
	public static class Windowing
		{
		/// <summary>
		/// Единый источник таймзоны Нью-Йорка для всего проекта.
		/// </summary>
		public static TimeZoneInfo NyTz => TimeZones.NewYork;

		/// <summary>
		/// True, если момент UTC соответствует открытию "NY morning bar":
		/// будний день и ровно 07:00 (зима) или 08:00 (DST) по NY локальному времени.
		/// </summary>
		public static bool IsNyMorning ( DateTime utc, TimeZoneInfo nyTz )
			{
			EnsureUtc (utc, nameof (utc));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var local = TimeZoneInfo.ConvertTimeFromUtc (utc, nyTz);
			if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				return false;

			// Ожидаемый час считается по локальному времени этого же момента,
			// иначе на границах DST можно получить неверную классификацию.
			int expectedHour = nyTz.IsDaylightSavingTime (local) ? 8 : 7;

			return local.Hour == expectedHour
				&& local.Minute == 0
				&& local.Second == 0;
			}

		public static bool IsNyMorning ( DateTime utc ) => IsNyMorning (utc, NyTz);

		/// <summary>
		/// Основной API: baseline-exit для дневной сделки.
		/// Weekend-entry запрещён: кидаем исключение (контракт строгий).
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc ) =>
			ComputeBaselineExitUtc (entryUtc, NyTz);

		/// <summary>
		/// То же, но с явной таймзоной.
		/// Baseline-exit = следующее "NY morning" минус 2 минуты.
		/// Для Friday переносим на ближайший рабочий день после уикенда.
		/// </summary>
		public static DateTime ComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz )
			{
			if (!TryComputeBaselineExitUtc (entryUtc, nyTz, out var exitUtc))
				throw new InvalidOperationException ($"[windowing] Weekend entry is not allowed: {entryUtc:O}.");

			return exitUtc;
			}

		/// <summary>
		/// Мягкий вариант для сплитов/фильтров:
		/// - на weekend возвращает false (exitUtc=default);
		/// - на будни возвращает true и валидный exitUtc.
		/// Важно: любые "невозможные" состояния (exit<=entry, bad Kind) не маскируем.
		/// </summary>
		public static bool TryComputeBaselineExitUtc ( DateTime entryUtc, TimeZoneInfo nyTz, out DateTime exitUtc )
			{
			EnsureUtc (entryUtc, nameof (entryUtc));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var entryLocal = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);

			if (entryLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
				{
				exitUtc = default;
				return false;
				}

			// Friday -> Monday (пропускаем выходные), иначе +1 день.
			int addDays = entryLocal.DayOfWeek == DayOfWeek.Friday ? 3 : 1;
			var targetDate = entryLocal.Date.AddDays (addDays);

			// DST определяем по "целевому дню", чтобы Sunday DST switch не ломал инвариант.
			var noon = new DateTime (targetDate.Year, targetDate.Month, targetDate.Day, 12, 0, 0, DateTimeKind.Unspecified);
			bool dst = nyTz.IsDaylightSavingTime (noon);

			int morningHour = dst ? 8 : 7;
			var nextMorningLocal = new DateTime (
				targetDate.Year, targetDate.Month, targetDate.Day,
				morningHour, 0, 0,
				DateTimeKind.Unspecified);

			// 2 минуты назад — чтобы окно заканчивалось ДО следующего утреннего бара.
			var exitLocal = nextMorningLocal.AddMinutes (-2);
			exitUtc = TimeZoneInfo.ConvertTimeToUtc (exitLocal, nyTz);

			if (exitUtc <= entryUtc)
				throw new InvalidOperationException ($"[windowing] Invalid baseline window: start={entryUtc:O}, end={exitUtc:O}.");

			return true;
			}

		private static void EnsureUtc ( DateTime dt, string name )
			{
			if (dt.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[windowing] {name} must be UTC. Got Kind={dt.Kind}.");
			}
		}
	}
