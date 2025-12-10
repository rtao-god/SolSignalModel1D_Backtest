namespace SolSignalModel1D_Backtest.Core.Infra
	{
	/// <summary>
	/// Утилиты для таймзон, используемых в моделях.
	/// </summary>
	public static class TimeZones
		{
		/// <summary>
		/// Таймзона Нью-Йорка (кеширована один раз на всё приложение).
		/// </summary>
		public static readonly TimeZoneInfo NewYork = ResolveNewYork ();

		/// <summary>
		/// Внутреннее разрешение таймзоны NY с учётом разных идентификаторов на платформах.
		/// </summary>
		private static TimeZoneInfo ResolveNewYork ()
			{
			try
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("America/New_York");
				}
			catch
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("Eastern Standard Time");
				}
			}
		}
	}
