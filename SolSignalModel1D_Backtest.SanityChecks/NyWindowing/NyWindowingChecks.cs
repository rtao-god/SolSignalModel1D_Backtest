using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.SanityChecks.Windowing
{
    /// <summary>
    /// Быстрые sanity-проверки time-contract.
    /// Возвращает список ошибок (если пусто — всё ок).
    /// </summary>
    public static class NyWindowingChecks
    {
        public static IReadOnlyList<string> RunBasic()
        {
            var errors = new List<string>();

            try
            {
                var nyTz = NyWindowing.NyTz;

                // Зима: 12:00 UTC == 07:00 NY (утро).
                var winterEntryUtcDt = new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc);
                var winterEntryUtc = new EntryUtc(new UtcInstant(winterEntryUtcDt));

                if (!NyWindowing.IsNyMorning(winterEntryUtc, nyTz))
                    errors.Add("[NyWindowing-check] winterEntryUtc is expected to be NY morning.");

                var winterExitUtc = NyWindowing.ComputeBaselineExitUtc(winterEntryUtc, nyTz).Value;
                var winterExitLocal = TimeZoneInfo.ConvertTimeFromUtc(winterExitUtc, nyTz);
                if (winterExitLocal.Hour != 6 || winterExitLocal.Minute != 58)
                    errors.Add($"[NyWindowing-check] winter exit local expected 06:58, got {winterExitLocal:O}.");

                // Лето: 12:00 UTC == 08:00 NY (утро).
                var summerEntryUtcDt = new DateTime(2024, 6, 10, 12, 0, 0, DateTimeKind.Utc);
                var summerEntryUtc = new EntryUtc(new UtcInstant(summerEntryUtcDt));

                if (!NyWindowing.IsNyMorning(summerEntryUtc, nyTz))
                    errors.Add("[NyWindowing-check] summerEntryUtc is expected to be NY morning.");

                var summerExitUtc = NyWindowing.ComputeBaselineExitUtc(summerEntryUtc, nyTz).Value;
                var summerExitLocal = TimeZoneInfo.ConvertTimeFromUtc(summerExitUtc, nyTz);
                if (summerExitLocal.Hour != 7 || summerExitLocal.Minute != 58)
                    errors.Add($"[NyWindowing-check] summer exit local expected 07:58, got {summerExitLocal:O}.");
            }
            catch (Exception ex)
            {
                errors.Add("[NyWindowing-check] Exception: " + ex.Message);
            }

            return errors;
        }
    }
}
