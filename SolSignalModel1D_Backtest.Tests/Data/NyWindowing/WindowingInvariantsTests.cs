using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using Xunit;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.Tests.Data.NyWindowing
{
    public sealed class NyWindowingInvariantsTests
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        [Fact]
        public void ComputeBaselineExitUtc_ThrowsOnWeekendEntry()
        {
            var entryLocal = new DateTime(2024, 3, 9, 8, 0, 0, DateTimeKind.Unspecified);
            var entryUtcDt = TimeZoneInfo.ConvertTimeToUtc(entryLocal, NyTz);
            var entryUtc = new EntryUtc(new UtcInstant(entryUtcDt));

            Assert.Throws<InvalidOperationException>(() =>
                CoreNyWindowing.ComputeBaselineExitUtc(entryUtc, NyTz));
        }

        [Fact]
        public void ComputeBaselineExitUtc_MovesFridayToNextBusinessMorning()
        {
            var entryLocal = new DateTime(2024, 3, 8, 8, 0, 0, DateTimeKind.Unspecified);
            var entryUtcDt = TimeZoneInfo.ConvertTimeToUtc(entryLocal, NyTz);
            var entryUtc = new EntryUtc(new UtcInstant(entryUtcDt));

            var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc(entryUtc, NyTz).Value;
            var exitLocal = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, NyTz);

            Assert.Equal(DayOfWeek.Monday, exitLocal.DayOfWeek);
            Assert.True(exitUtc > entryUtc.Value);
        }

        [Fact]
        public void IsNyMorning_TrueOnlyForBusinessMorningSlots()
        {
            var mondayMorningLocal = new DateTime(2024, 3, 11, 8, 0, 0, DateTimeKind.Unspecified);
            var mondayMorningUtcDt = TimeZoneInfo.ConvertTimeToUtc(mondayMorningLocal, NyTz);
            var mondayMorningUtc = new EntryUtc(new UtcInstant(mondayMorningUtcDt));

            var mondayNoonLocal = new DateTime(2024, 3, 11, 12, 0, 0, DateTimeKind.Unspecified);
            var mondayNoonUtcDt = TimeZoneInfo.ConvertTimeToUtc(mondayNoonLocal, NyTz);
            var mondayNoonUtc = new EntryUtc(new UtcInstant(mondayNoonUtcDt));

            var saturdayLocal = new DateTime(2024, 3, 9, 8, 0, 0, DateTimeKind.Unspecified);
            var saturdayUtcDt = TimeZoneInfo.ConvertTimeToUtc(saturdayLocal, NyTz);
            var saturdayUtc = new EntryUtc(new UtcInstant(saturdayUtcDt));

            Assert.True(CoreNyWindowing.IsNyMorning(mondayMorningUtc, NyTz));
            Assert.False(CoreNyWindowing.IsNyMorning(mondayNoonUtc, NyTz));
            Assert.False(CoreNyWindowing.IsNyMorning(saturdayUtc, NyTz));
        }
    }
}

