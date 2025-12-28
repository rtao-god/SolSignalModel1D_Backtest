namespace SolSignalModel1D_Backtest.Tests.Data.NyWindowing.NyTz
	{
	/// <summary>
	/// Резолвер таймзоны Нью-Йорка для тестов.
	/// </summary>
	internal static class NyTimeZone
		{
		public static TimeZoneInfo Value { get; } = Resolve ();

		private static TimeZoneInfo Resolve ()
			{
			// На Linux/macOS обычно IANA id, на Windows — Windows id.
			// Делаем две попытки, и если обе неудачны — валим тесты с явной причиной.
			try
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("America/New_York");
				}
			catch (TimeZoneNotFoundException)
				{
				// fallback ниже
				}
			catch (InvalidTimeZoneException)
				{
				throw;
				}

			try
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("Eastern Standard Time");
				}
			catch (Exception ex)
				{
				throw new InvalidOperationException (
					"Не удалось найти таймзону Нью-Йорка (America/New_York или Eastern Standard Time). " +
					"Проверь установку tzdata/Windows time zones.",
					ex);
				}
			}
		}
	}

