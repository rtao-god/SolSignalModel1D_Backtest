using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Tests.Causal
{
    public sealed class NyWindowingContractTests
    {
        [Fact]
        public void ComputeBaselineExitUtc_Throws_ForWeekendEntry()
        {
            var entryUtc = new EntryUtc(new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc));

            var ex = Assert.Throws<InvalidOperationException>(
                () => NyWindowing.ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz));

            Assert.Contains("weekend", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryComputeBaselineExitUtc_ReturnsFalse_ForWeekendEntry()
        {
            var entryUtc = new EntryUtc(new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc));

            bool ok = NyWindowing.TryComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz, out var exitUtc);

            Assert.False(ok);
            Assert.Equal(default, exitUtc);
        }

        [Fact]
        public void WinterWeekday_ExitIs_0658_NyLocal_NextDay()
        {
            var entryUtc = new EntryUtc(new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc)); // Mon

            Assert.True(NyWindowing.IsNyMorning(entryUtc, NyWindowing.NyTz));

            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz).Value;
            Assert.True(exitUtc > entryUtc.Value);

            var nyExit = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, NyWindowing.NyTz);

            Assert.Equal(6, nyExit.Hour);
            Assert.Equal(58, nyExit.Minute);

            var nyEntry = TimeZoneInfo.ConvertTimeFromUtc(entryUtc.Value, NyWindowing.NyTz);
            Assert.Equal(nyEntry.Date.AddDays(1), nyExit.Date);
        }

        [Fact]
        public void SummerWeekday_ExitIs_0758_NyLocal_NextDay()
        {
            var entryUtc = new EntryUtc(new DateTime(2024, 6, 10, 12, 0, 0, DateTimeKind.Utc)); // Mon

            Assert.True(NyWindowing.IsNyMorning(entryUtc, NyWindowing.NyTz));

            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz).Value;
            var nyExit = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, NyWindowing.NyTz);

            Assert.Equal(7, nyExit.Hour);
            Assert.Equal(58, nyExit.Minute);

            var nyEntry = TimeZoneInfo.ConvertTimeFromUtc(entryUtc.Value, NyWindowing.NyTz);
            Assert.Equal(nyEntry.Date.AddDays(1), nyExit.Date);
        }

        [Fact]
        public void Friday_GoesToNextBusinessMorning()
        {
            var entryUtc = new EntryUtc(new DateTime(2024, 1, 5, 12, 0, 0, DateTimeKind.Utc)); // Fri

            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz).Value;
            var nyExit = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, NyWindowing.NyTz);

            Assert.Equal(DayOfWeek.Monday, nyExit.DayOfWeek);
            Assert.Equal(6, nyExit.Hour);
            Assert.Equal(58, nyExit.Minute);
        }

        [Fact]
        public void Split_UsesBaselineExit_AndWeekendsGoToExcluded()
        {
            var trainUntilExitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var items = new List<EntryUtc>
            {
                new EntryUtc(new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc)), // Sat
				new EntryUtc(new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc)), // Mon
				new EntryUtc(new DateTime(2024, 1, 7, 12, 0, 0, DateTimeKind.Utc)), // Sun
				new EntryUtc(new DateTime(2024, 1, 9, 12, 0, 0, DateTimeKind.Utc)), // Tue
			};

            var ordered = items.OrderBy(x => x.Value).ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: e => e,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: TimeZones.NewYork);

            Assert.Equal(2, split.Train.Count);
            Assert.Empty(split.Oos);
            Assert.Equal(2, split.Excluded.Count);
        }
    }
}
