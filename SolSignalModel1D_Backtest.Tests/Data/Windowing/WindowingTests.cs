using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using Xunit;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Data.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing
	{
	public sealed class WindowingTests
		{
		[Fact]
		public void ComputeBaselineExitUtc_Weekday_GoesToNextMorningMinusTwoMinutes ()
			{
			var nyTz = CoreWindowing.NyTz;

			var entryLocal = new DateTime (2024, 1, 8, 7, 0, 0, DateTimeKind.Unspecified); // понедельник (зима, без DST)
			Assert.False (nyTz.IsDaylightSavingTime (entryLocal));

			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocal, nyTz);

			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			var exitLocal = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, nyTz);

			Assert.Equal (entryLocal.Date.AddDays (1), exitLocal.Date);
			Assert.Equal (7, exitLocal.Hour);
			Assert.Equal (58, exitLocal.Minute);
			}

		[Fact]
		public void ComputeBaselineExitUtc_Friday_GoesToMondayMorning ()
			{
			var nyTz = CoreWindowing.NyTz;

			var entryLocal = new DateTime (2024, 1, 5, 7, 0, 0, DateTimeKind.Unspecified); // пятница
			Assert.Equal (DayOfWeek.Friday, entryLocal.DayOfWeek);
			Assert.False (nyTz.IsDaylightSavingTime (entryLocal));

			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocal, nyTz);

			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			var exitLocal = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, nyTz);

			Assert.Equal (DayOfWeek.Monday, exitLocal.DayOfWeek);
			Assert.Equal (7, exitLocal.Hour);
			Assert.Equal (58, exitLocal.Minute);
			}

		[Fact]
		public void ComputeBaselineExitUtc_Throws_OnWeekendEntry ()
			{
			var nyTz = CoreWindowing.NyTz;

			var saturdayLocal = new DateTime (2024, 1, 6, 12, 0, 0, DateTimeKind.Unspecified);
			Assert.Equal (DayOfWeek.Saturday, saturdayLocal.DayOfWeek);

			var saturdayUtc = TimeZoneInfo.ConvertTimeToUtc (saturdayLocal, nyTz);

			Assert.Throws<InvalidOperationException> (() => CoreWindowing.ComputeBaselineExitUtc (saturdayUtc, nyTz));
			}

		[Fact]
		public void FilterNyMorningOnly_RespectsDst_AndSkipsWeekends ()
			{
			var nyTz = CoreWindowing.NyTz;

			var candles = new List<Candle6h> ();

			// Зима: утро = 07:00
			var winterMorningLocal = new DateTime (2024, 1, 9, 7, 0, 0, DateTimeKind.Unspecified);
			Assert.False (nyTz.IsDaylightSavingTime (winterMorningLocal));
			var winterMorningUtc = TimeZoneInfo.ConvertTimeToUtc (winterMorningLocal, nyTz);

			candles.Add (new Candle6h { OpenTimeUtc = winterMorningUtc, Open = 100, High = 101, Low = 99, Close = 100.5 });

			// Лето (DST): утро = 08:00
			var summerMorningLocal = new DateTime (2024, 6, 10, 8, 0, 0, DateTimeKind.Unspecified);
			Assert.True (nyTz.IsDaylightSavingTime (summerMorningLocal));
			var summerMorningUtc = TimeZoneInfo.ConvertTimeToUtc (summerMorningLocal, nyTz);

			candles.Add (new Candle6h { OpenTimeUtc = summerMorningUtc, Open = 200, High = 202, Low = 198, Close = 201 });

			// Суббота — не должна попасть
			var weekendLocal = new DateTime (2024, 1, 6, 7, 0, 0, DateTimeKind.Unspecified);
			Assert.Equal (DayOfWeek.Saturday, weekendLocal.DayOfWeek);
			var weekendUtc = TimeZoneInfo.ConvertTimeToUtc (weekendLocal, nyTz);

			candles.Add (new Candle6h { OpenTimeUtc = weekendUtc, Open = 150, High = 151, Low = 149, Close = 150.5 });

			var filtered = CoreWindowing.FilterNyMorningOnly (candles, nyTz);

			Assert.Equal (2, filtered.Count);
			Assert.Contains (filtered, c => c.OpenTimeUtc == winterMorningUtc);
			Assert.Contains (filtered, c => c.OpenTimeUtc == summerMorningUtc);
			Assert.DoesNotContain (filtered, c => c.OpenTimeUtc == weekendUtc);
			}

		[Fact]
		public void IsNyMorning_True_OnlyForMorningBar ()
			{
			var nyTz = CoreWindowing.NyTz;

			// Зима: утро 07:00, день 13:00
			var winterMorningLocal = new DateTime (2024, 1, 10, 7, 0, 0, DateTimeKind.Unspecified);
			var winterDayLocal = new DateTime (2024, 1, 10, 13, 0, 0, DateTimeKind.Unspecified);

			var winterMorningUtc = TimeZoneInfo.ConvertTimeToUtc (winterMorningLocal, nyTz);
			var winterDayUtc = TimeZoneInfo.ConvertTimeToUtc (winterDayLocal, nyTz);

			Assert.True (CoreWindowing.IsNyMorning (winterMorningUtc, nyTz));
			Assert.False (CoreWindowing.IsNyMorning (winterDayUtc, nyTz));

			// Лето (DST): утро 08:00
			var summerMorningLocal = new DateTime (2024, 6, 11, 8, 0, 0, DateTimeKind.Unspecified);
			Assert.True (nyTz.IsDaylightSavingTime (summerMorningLocal));
			var summerMorningUtc = TimeZoneInfo.ConvertTimeToUtc (summerMorningLocal, nyTz);

			Assert.True (CoreWindowing.IsNyMorning (summerMorningUtc, nyTz));
			}

		[Fact]
		public void BuildSpacedTest_TakesBlocksFromEnd_WithSkips_AndKeepsOrder ()
			{
			var rows = new List<DataRow> ();
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 10; i++)
				{
				rows.Add (new DataRow { Date = start.AddDays (i), Label = i });
				}

			var spaced = CoreWindowing.BuildSpacedTest (rows, take: 3, skip: 2, blocks: 2);

			Assert.Equal (6, spaced.Count);

			var dates = spaced.Select (r => r.Date).ToList ();
			Assert.True (dates.SequenceEqual (dates.OrderBy (d => d)));

			var labels = spaced.Select (r => r.Label).ToArray ();
			Assert.Equal (new[] { 2, 3, 4, 7, 8, 9 }, labels);
			}

		[Fact]
		public void GroupByBlocks_SplitsIntoConsecutiveBlocks ()
			{
			var rows = new List<DataRow> ();
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 10; i++)
				{
				rows.Add (new DataRow { Date = start.AddDays (i), Label = i });
				}

			var blocks = CoreWindowing.GroupByBlocks (rows, blockSize: 4).ToList ();

			Assert.Equal (3, blocks.Count);
			Assert.Equal (new[] { 0, 1, 2, 3 }, blocks[0].Select (r => r.Label));
			Assert.Equal (new[] { 4, 5, 6, 7 }, blocks[1].Select (r => r.Label));
			Assert.Equal (new[] { 8, 9 }, blocks[2].Select (r => r.Label));
			}
		}
	}
