using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Causal
{
    /// <summary>
    /// Контракт NyWindowing + split по baseline-exit:
    /// - baseline-exit не определён для weekend-entry (Compute* бросает, Try* возвращает false);
    /// - baseline-exit = следующее NY-утро (07:00 зимой / 08:00 летом) минус 2 минуты (06:58 / 07:58);
    /// - Friday уходит на следующий рабочий день после уикенда;
    /// - split должен относить weekend в Excluded.
    /// </summary>
    public sealed class NyWindowingAndTrainBoundaryTests
    {
        [Fact]
        public void ComputeBaselineExitUtc_Throws_ForWeekendEntry()
        {
            var entryUtc = new EntryUtc(new DateTime(2020, 2, 22, 15, 0, 0, DateTimeKind.Utc)); // Sat in NY

            var ex = Assert.Throws<InvalidOperationException>(
                () => NyWindowing.ComputeBaselineExitUtc(entryUtc, TimeZones.NewYork));

            Assert.Contains("weekend", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ComputeBaselineExitUtc_ForWeekday_EndsAt_0658_NyLocal_InWinter()
        {
            // 2020-02-24 — зима в NY (baseline morning = 07:00, exit=06:58).
            var entryUtc = new EntryUtc(new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc));

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
            var entryUtc = new EntryUtc(new DateTime(2020, 2, 28, 15, 0, 0, DateTimeKind.Utc)); // Fri

            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, TimeZones.NewYork).Value;

            var ny = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, TimeZones.NewYork);
            Assert.Equal(DayOfWeek.Monday, ny.DayOfWeek);

            // Зима: 06:58 NY.
            Assert.Equal(6, ny.Hour);
            Assert.Equal(58, ny.Minute);
        }

        [Fact]
        public void TryComputeBaselineExitUtc_ReturnsFalse_ForWeekend()
        {
            var weekendEntryUtc = new EntryUtc(new DateTime(2020, 2, 22, 15, 0, 0, DateTimeKind.Utc));

            bool ok = NyWindowing.TryComputeBaselineExitUtc(weekendEntryUtc, TimeZones.NewYork, out var exitUtc);

            Assert.False(ok);
            Assert.Equal(default, exitUtc);
        }

        [Fact]
        public void Split_PutsWeekendsIntoExcluded()
        {
            var trainUntilUtc = new TrainUntilUtc(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var items = new List<EntryUtc>
                {
                new EntryUtc (new DateTime (2020, 2, 22, 15, 0, 0, DateTimeKind.Utc)), // Sat
				new EntryUtc (new DateTime (2020, 2, 24, 15, 0, 0, DateTimeKind.Utc)), // Mon
				new EntryUtc (new DateTime (2020, 2, 23, 15, 0, 0, DateTimeKind.Utc)), // Sun
				new EntryUtc (new DateTime (2020, 2, 25, 15, 0, 0, DateTimeKind.Utc)), // Tue
				};

            var split = TrainSplitByBaselineExit.Split(
                items: items,
                entrySelector: e => e,
                trainUntilUtc: trainUntilUtc,
                nyTz: TimeZones.NewYork);

            Assert.Equal(2, split.Train.Count);
            Assert.Empty(split.Oos);
            Assert.Equal(2, split.Excluded.Count);
        }
    }
}
