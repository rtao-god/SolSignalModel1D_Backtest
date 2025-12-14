using System;
using SolSignalModel1D_Backtest.Tests.Data.Windowing.NyTz;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing.ComputeBaselineExitUtc
	{
	/// <summary>
	/// Тестовый расчёт "baseline exit" по NY-утру.
	/// Цель: дать тестам единый, детерминированный контракт по времени,
	/// чтобы проверки границ train/oos не зависели от случайных локальных преобразований.
	/// </summary>
	internal static class ComputeBaselineExitUtc
		{
		// ПРЕДПОЛОЖЕНИЕ (если в проде другое NY-время, поменяй тут и в прод-контракте синхронно):
		// "утро" в Нью-Йорке = 08:00 local.
		private static readonly TimeSpan DefaultNyMorningLocalTime = new (8, 0, 0);

		/// <summary>
		/// Для заданного entryUtc возвращает следующее NY-утро в UTC.
		/// В пятницу окно переносится через выходные (Fri -> Mon).
		/// </summary>
		public static DateTime ForEntryUtc ( DateTime entryUtc, TimeSpan? nyMorningLocalTime = null )
			{
			if (entryUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("Ожидается DateTime в UTC.", nameof (entryUtc));

			var tz = NyTimeZone.Value;
			var nyLocal = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, tz);

			// Инвариант: базовое окно закрывается в "следующее NY-утро".
			// В пятницу (и если вдруг попали на субботу) перескакиваем к понедельнику.
			var addDays = nyLocal.DayOfWeek switch
				{
					DayOfWeek.Friday => 3,
					DayOfWeek.Saturday => 2,
					_ => 1,
					};

			var exitLocalDate = nyLocal.Causal.DateUtc.AddDays (addDays);
			var morning = nyMorningLocalTime ?? DefaultNyMorningLocalTime;
			var exitLocal = exitLocalDate + morning;

			var exitUtc = TimeZoneInfo.ConvertTimeToUtc (exitLocal, tz);
			return DateTime.SpecifyKind (exitUtc, DateTimeKind.Utc);
			}
		}
	}
