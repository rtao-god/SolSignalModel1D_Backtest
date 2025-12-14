using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.SanityChecks.Windowing
	{
	/// <summary>
	/// Быстрые sanity-проверки time-contract.
	/// Возвращает список ошибок (если пусто — всё ок).
	/// Важно: это НЕ замена unit-тестам; цель — ловить регрессии в рантайме.
	/// </summary>
	public static class WindowingChecks
		{
		public static IReadOnlyList<string> RunBasic ()
			{
			var errors = new List<string> ();

			try
				{
				var nyTz = CoreWindowing.NyTz;

				// Зима: 12:00 UTC == 07:00 NY (утро).
				var winterEntryUtc = new DateTime (2024, 1, 8, 12, 0, 0, DateTimeKind.Utc);
				if (!CoreWindowing.IsNyMorning (winterEntryUtc, nyTz))
					errors.Add ("[windowing-check] winterEntryUtc is expected to be NY morning.");

				var winterExitUtc = CoreWindowing.ComputeBaselineExitUtc (winterEntryUtc, nyTz);
				var winterExitLocal = TimeZoneInfo.ConvertTimeFromUtc (winterExitUtc, nyTz);
				if (winterExitLocal.Hour != 6 || winterExitLocal.Minute != 58)
					errors.Add ($"[windowing-check] winter exit local expected 06:58, got {winterExitLocal:O}.");

				// Лето: 12:00 UTC == 08:00 NY (утро).
				var summerEntryUtc = new DateTime (2024, 6, 10, 12, 0, 0, DateTimeKind.Utc);
				if (!CoreWindowing.IsNyMorning (summerEntryUtc, nyTz))
					errors.Add ("[windowing-check] summerEntryUtc is expected to be NY morning.");

				var summerExitUtc = CoreWindowing.ComputeBaselineExitUtc (summerEntryUtc, nyTz);
				var summerExitLocal = TimeZoneInfo.ConvertTimeFromUtc (summerExitUtc, nyTz);
				if (summerExitLocal.Hour != 7 || summerExitLocal.Minute != 58)
					errors.Add ($"[windowing-check] summer exit local expected 07:58, got {summerExitLocal:O}.");
				}
			catch (Exception ex)
				{
				errors.Add ("[windowing-check] Exception: " + ex.Message);
				}

			return errors;
			}
		}
	}
