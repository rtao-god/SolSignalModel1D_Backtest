using System;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
{
    public static class NyNyWindowingTestUtils
    {
        public static TimeZoneInfo NewYorkTz { get; } = ResolveNewYorkTimeZoneOrThrow();

        public static EntryUtc EntryUtcFromUtcOrThrow(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[test] Expected UTC DateTime. Got Kind={utc.Kind}, t={utc:O}.");

            return new EntryUtc(new UtcInstant(utc));
        }

        public static EntryUtc EntryUtcFromNyDayOrThrow(int year, int month, int day)
        {
            var nyDay = new NyTradingDay(new DateOnly(year, month, day));
            return NyWindowing.ComputeEntryUtcFromNyDayOrThrow(nyDay, NewYorkTz);
        }

        private static TimeZoneInfo ResolveNewYorkTimeZoneOrThrow()
        {
            // Cross-platform:
            // - Linux/macOS: IANA "America/New_York"
            // - Windows: "Eastern Standard Time"
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
        }
    }
}
