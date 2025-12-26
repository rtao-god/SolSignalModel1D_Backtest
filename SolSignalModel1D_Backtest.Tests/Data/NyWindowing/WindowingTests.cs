using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing
{
    public sealed class NyWindowingTests
    {
        [Fact]
        public void ComputeBaselineExitUtc_Weekday_GoesToNextNyMorningMinusTwoMinutes()
        {
            var nyTz = CoreNyWindowing.NyTz;

            var entryLocal = new DateTime(2024, 1, 8, 7, 0, 0, DateTimeKind.Unspecified); // Mon, winter
            Assert.False(nyTz.IsDaylightSavingTime(entryLocal));

            var entryUtcDt = TimeZoneInfo.ConvertTimeToUtc(entryLocal, nyTz);
            var entryUtc = NyWindowingTestUtils.EntryUtcFromUtcOrThrow(entryUtcDt);

            var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc(entryUtc, nyTz).Value;
            var exitLocal = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, nyTz);

            Assert.Equal(entryLocal.Date.AddDays(1), exitLocal.Date);
            Assert.Equal(6, exitLocal.Hour);
            Assert.Equal(58, exitLocal.Minute);
        }

        [Fact]
        public void ComputeBaselineExitUtc_Friday_GoesToMondayNyMorningMinusTwoMinutes()
        {
            var nyTz = CoreNyWindowing.NyTz;

            var entryLocal = new DateTime(2024, 1, 5, 7, 0, 0, DateTimeKind.Unspecified); // Fri, winter
            Assert.Equal(DayOfWeek.Friday, entryLocal.DayOfWeek);
            Assert.False(nyTz.IsDaylightSavingTime(entryLocal));

            var entryUtcDt = TimeZoneInfo.ConvertTimeToUtc(entryLocal, nyTz);
            var entryUtc = NyWindowingTestUtils.EntryUtcFromUtcOrThrow(entryUtcDt);

            var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc(entryUtc, nyTz).Value;
            var exitLocal = TimeZoneInfo.ConvertTimeFromUtc(exitUtc, nyTz);

            Assert.Equal(DayOfWeek.Monday, exitLocal.DayOfWeek);
            Assert.Equal(6, exitLocal.Hour);
            Assert.Equal(58, exitLocal.Minute);
        }

        [Fact]
        public void ComputeBaselineExitUtc_Throws_OnWeekendEntry()
        {
            var nyTz = CoreNyWindowing.NyTz;

            var saturdayLocal = new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Unspecified);
            Assert.Equal(DayOfWeek.Saturday, saturdayLocal.DayOfWeek);

            var saturdayUtcDt = TimeZoneInfo.ConvertTimeToUtc(saturdayLocal, nyTz);
            var saturdayUtc = NyWindowingTestUtils.EntryUtcFromUtcOrThrow(saturdayUtcDt);

            Assert.Throws<InvalidOperationException>(() => CoreNyWindowing.ComputeBaselineExitUtc(saturdayUtc, nyTz));
        }

        [Fact]
        public void FilterNyMorningOnly_RespectsDst_AndSkipsWeekends()
        {
            var nyTz = CoreNyWindowing.NyTz;

            var candles = new List<Candle6h>();

            var winterMorningLocal = new DateTime(2024, 1, 9, 7, 0, 0, DateTimeKind.Unspecified);
            Assert.False(nyTz.IsDaylightSavingTime(winterMorningLocal));
            var winterMorningUtc = TimeZoneInfo.ConvertTimeToUtc(winterMorningLocal, nyTz);
            candles.Add(new Candle6h { OpenTimeUtc = winterMorningUtc, Open = 100, High = 101, Low = 99, Close = 100.5 });

            var summerMorningLocal = new DateTime(2024, 6, 10, 8, 0, 0, DateTimeKind.Unspecified);
            Assert.True(nyTz.IsDaylightSavingTime(summerMorningLocal));
            var summerMorningUtc = TimeZoneInfo.ConvertTimeToUtc(summerMorningLocal, nyTz);
            candles.Add(new Candle6h { OpenTimeUtc = summerMorningUtc, Open = 200, High = 202, Low = 198, Close = 201 });

            var weekendLocal = new DateTime(2024, 1, 6, 7, 0, 0, DateTimeKind.Unspecified);
            Assert.Equal(DayOfWeek.Saturday, weekendLocal.DayOfWeek);
            var weekendUtc = TimeZoneInfo.ConvertTimeToUtc(weekendLocal, nyTz);
            candles.Add(new Candle6h { OpenTimeUtc = weekendUtc, Open = 150, High = 151, Low = 149, Close = 150.5 });

            var filtered = NyWindowingTestSeriesUtils.FilterNyMorningOnly(candles, nyTz);

            Assert.Equal(2, filtered.Count);
            Assert.Contains(filtered, c => c.OpenTimeUtc == winterMorningUtc);
            Assert.Contains(filtered, c => c.OpenTimeUtc == summerMorningUtc);
            Assert.DoesNotContain(filtered, c => c.OpenTimeUtc == weekendUtc);
        }

        [Fact]
        public void IsNyMorning_True_OnlyForMorningBar()
        {
            var nyTz = CoreNyWindowing.NyTz;

            var winterMorningLocal = new DateTime(2024, 1, 10, 7, 0, 0, DateTimeKind.Unspecified);
            var winterDayLocal = new DateTime(2024, 1, 10, 13, 0, 0, DateTimeKind.Unspecified);

            var winterMorningUtc = NyWindowingTestUtils.EntryUtcFromUtcOrThrow(TimeZoneInfo.ConvertTimeToUtc(winterMorningLocal, nyTz));
            var winterDayUtc = NyWindowingTestUtils.EntryUtcFromUtcOrThrow(TimeZoneInfo.ConvertTimeToUtc(winterDayLocal, nyTz));

            Assert.True(CoreNyWindowing.IsNyMorning(winterMorningUtc, nyTz));
            Assert.False(CoreNyWindowing.IsNyMorning(winterDayUtc, nyTz));

            var summerMorningLocal = new DateTime(2024, 6, 11, 8, 0, 0, DateTimeKind.Unspecified);
            Assert.True(nyTz.IsDaylightSavingTime(summerMorningLocal));
            var summerMorningUtc = NyWindowingTestUtils.EntryUtcFromUtcOrThrow(TimeZoneInfo.ConvertTimeToUtc(summerMorningLocal, nyTz));

            Assert.True(CoreNyWindowing.IsNyMorning(summerMorningUtc, nyTz));
        }

        [Fact]
        public void BuildSpacedTest_TakesBlocksFromEnd_WithSkips_AndKeepsOrder()
        {
            var rows = new List<DummyRow>();
            var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (int i = 0; i < 10; i++)
                rows.Add(new DummyRow { DateUtc = start.AddDays(i), Label = i });

            var spaced = NyWindowingTestSeriesUtils.BuildSpacedTest(
                rows,
                take: 3,
                skip: 2,
                blocks: 2,
                dateSelector: r => r.DateUtc);

            Assert.Equal(6, spaced.Count);

            var dates = spaced.Select(r => r.DateUtc).ToList();
            Assert.True(dates.SequenceEqual(dates.OrderBy(d => d)));

            var labels = spaced.Select(r => r.Label).ToArray();
            Assert.Equal(new[] { 2, 3, 4, 7, 8, 9 }, labels);
        }

        [Fact]
        public void GroupByBlocks_SplitsIntoConsecutiveBlocks()
        {
            var rows = new List<DummyRow>();
            var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (int i = 0; i < 10; i++)
                rows.Add(new DummyRow { DateUtc = start.AddDays(i), Label = i });

            var blocks = NyWindowingTestSeriesUtils.GroupByBlocks(rows, blockSize: 4).ToList();

            Assert.Equal(3, blocks.Count);
            Assert.Equal(new[] { 0, 1, 2, 3 }, blocks[0].Select(r => r.Label));
            Assert.Equal(new[] { 4, 5, 6, 7 }, blocks[1].Select(r => r.Label));
            Assert.Equal(new[] { 8, 9 }, blocks[2].Select(r => r.Label));
        }

        private sealed class DummyRow
        {
            public DateTime DateUtc { get; init; }
            public int Label { get; init; }
        }
    }
}
