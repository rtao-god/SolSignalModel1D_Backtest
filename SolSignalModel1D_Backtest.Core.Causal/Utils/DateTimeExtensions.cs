namespace SolSignalModel1D_Backtest.Core.Causal.Utils
	{
	public static class DateTimeExtensions
		{
		/// <summary>
		/// Выходные по UTC — суббота и воскресенье.
		/// </summary>
		public static bool IsWeekendUtc ( this DateTime dtUtc )
			{
			var day = dtUtc.DayOfWeek;
			return day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
			}

		/// <summary>
		/// Обрезает до часа.
		/// </summary>
		public static DateTime TruncateToHourUtc ( this DateTime dtUtc )
			=> new DateTime (dtUtc.Year, dtUtc.Month, dtUtc.Day, dtUtc.Hour, 0, 0, DateTimeKind.Utc);
		}
	}
