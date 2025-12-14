using System;

namespace SolSignalModel1D_Backtest.Core.Infra
	{
	/// <summary>
	/// Единая точка получения TimeZoneInfo для проекта.
	/// Важно: идентификаторы таймзон различаются между Windows и Linux.
	/// </summary>
	public static class TimeZones
		{
		private static readonly Lazy<TimeZoneInfo> _newYork = new Lazy<TimeZoneInfo> (ResolveNewYork);

		/// <summary>
		/// Нью-Йорк (America/New_York). Используется для DST-логики утреннего бара и baseline-окна.
		/// </summary>
		public static TimeZoneInfo NewYork => _newYork.Value;

		private static TimeZoneInfo ResolveNewYork ()
			{
			// Windows ID
			try
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("Eastern Standard Time");
				}
			catch
				{
				// ignore → пробуем IANA
				}

			// Linux/macOS IANA ID
			try
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("America/New_York");
				}
			catch (Exception ex)
				{
				throw new InvalidOperationException (
					"[tz] Cannot resolve New York timezone. " +
					"Try installing tzdata (Linux) or ensure system time zones are available.",
					ex);
				}
			}
		}
	}
