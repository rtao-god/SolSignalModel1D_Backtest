using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Causal
{
    public sealed class NyWindowingAndTrainBoundaryTests
    {
        [Fact]
        public void ComputeBaselineExitUtc_Throws_ForWeekendEntry()
        {
            var entryUtc = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 22, 15, 0, 0, DateTimeKind.Utc))); // Sat in NY

            var ex = Assert.Throws<InvalidOperationException>(
                () => NyWindowing.ComputeBaselineExitUtc(entryUtc, TimeZones.NewYork));

            Assert.Contains("weekend", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ComputeBaselineExitUtc_ForWeekday_EndsAt_0658_NyLocal_InWinter()
        {
            var entryUtc = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc)));
            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, TimeZones.NewYork).Value;

            Assert.True(exitUtc > entryUtc.Value);

            var exitNy = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, TimeZones.NewYork);
            Assert.Equal(6, exitNy.Hour);
            Assert.Equal(58, exitNy.Minute);

            var entryNy = TimeZoneInfo.ConvertTimeFromUtc(entryUtc.Value, TimeZones.NewYork);
            Assert.True(exitNy.Date >= entryNy.Date.AddDays(1));
        }

        [Fact]
        public void ComputeBaselineExitUtc_ForFriday_GoesToNextBusinessMorning()
        {
            var entryUtc = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 28, 15, 0, 0, DateTimeKind.Utc))); // Fri
            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, TimeZones.NewYork).Value;

            var ny = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, TimeZones.NewYork);
            Assert.Equal(DayOfWeek.Monday, ny.DayOfWeek);
            Assert.Equal(6, ny.Hour);
            Assert.Equal(58, ny.Minute);
        }

        [Fact]
        public void TryComputeBaselineExitUtc_ReturnsFalse_ForWeekend()
        {
            var weekendEntryUtc = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 22, 15, 0, 0, DateTimeKind.Utc)));

            bool ok = NyWindowing.TryComputeBaselineExitUtc(weekendEntryUtc, TimeZones.NewYork, out var exitUtc);

            Assert.False(ok);
            Assert.Equal(default, exitUtc);
        }

        [Fact]
        public void Split_PutsWeekendsIntoExcluded()
        {
            var trainUntilExitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var items = new List<EntryUtc>
            {
                new EntryUtc(new UtcInstant(new DateTime(2020, 2, 22, 15, 0, 0, DateTimeKind.Utc))), // Sat
				new EntryUtc(new UtcInstant(new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc))), // Mon
				new EntryUtc(new UtcInstant(new DateTime(2020, 2, 23, 15, 0, 0, DateTimeKind.Utc))), // Sun
				new EntryUtc(new UtcInstant(new DateTime(2020, 2, 25, 15, 0, 0, DateTimeKind.Utc))), // Tue
			};

            items.Sort((a, b) => a.Value.CompareTo(b.Value));

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: items,
                entrySelector: e => e,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: TimeZones.NewYork);

            Assert.Equal(2, split.Train.Count);
            Assert.Empty(split.Oos);
            Assert.Equal(2, split.Excluded.Count);
        }
    }
}
