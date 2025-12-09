using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using Xunit;

// Алиас на боевой класс Windowing, чтобы не конфликтовать с namespace тестов.
using CoreWindowing = SolSignalModel1D_Backtest.Core.Data.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing
	{
	/// <summary>
	/// Тесты для утилит Windowing:
	/// - корректный baseline-exit (включая пятницу → понедельник);
	/// - корректная фильтрация NY-окон (утро / train);
	/// - корректная работа вспомогательных методов (IsNyMorning, BuildSpacedTest, GroupByBlocks).
	/// </summary>
	public sealed class WindowingTests
		{
		/// <summary>
		/// Хелпер: делает UTC-время из локального NY-времени.
		/// </summary>
		private static DateTime NyLocalToUtc ( int year, int month, int day, int hour, int minute = 0 )
			{
			var nyTz = CoreWindowing.NyTz;
			var local = new DateTime (year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
			return TimeZoneInfo.ConvertTimeToUtc (local, nyTz);
			}

		[Fact]
		public void ComputeBaselineExitUtc_Weekday_GoesToNextMorningMinusTwoMinutes ()
			{
			// Берём зимний будний день, когда DST нет → утренний бар 07:00.
			var nyTz = CoreWindowing.NyTz;

			var entryLocal = new DateTime (2024, 1, 8, 7, 0, 0, DateTimeKind.Unspecified); // вторник
			Assert.False (nyTz.IsDaylightSavingTime (entryLocal));

			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocal, nyTz);

			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			var exitLocal = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, nyTz);

			// Должны уйти на следующее утро:
			// дата +1 день, время ~08:00 - 2 минуты = 07:58 локального.
			Assert.Equal (entryLocal.Date.AddDays (1), exitLocal.Date);
			Assert.Equal (7, exitLocal.Hour);
			Assert.Equal (58, exitLocal.Minute);
			}

		[Fact]
		public void ComputeBaselineExitUtc_Friday_GoesToMondayMorning ()
			{
			var nyTz = CoreWindowing.NyTz;

			// Пятница зимой, утренний бар 07:00 (нет DST).
			var entryLocal = new DateTime (2024, 1, 5, 7, 0, 0, DateTimeKind.Unspecified);
			Assert.Equal (DayOfWeek.Friday, entryLocal.DayOfWeek);
			Assert.False (nyTz.IsDaylightSavingTime (entryLocal));

			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocal, nyTz);

			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			var exitLocal = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, nyTz);

			// Должны попасть в первый рабочий день после выходных — понедельник.
			Assert.Equal (DayOfWeek.Monday, exitLocal.DayOfWeek);

			// Время ~08:00 - 2 минуты.
			Assert.Equal (7, exitLocal.Hour);
			Assert.Equal (58, exitLocal.Minute);
			}

		[Fact]
		public void ComputeBaselineExitUtc_Throws_OnWeekendEntry ()
			{
			var nyTz = CoreWindowing.NyTz;

			// Любое время в субботу.
			var saturdayLocal = new DateTime (2024, 1, 6, 12, 0, 0, DateTimeKind.Unspecified);
			Assert.Equal (DayOfWeek.Saturday, saturdayLocal.DayOfWeek);

			var saturdayUtc = TimeZoneInfo.ConvertTimeToUtc (saturdayLocal, nyTz);

			Assert.Throws<InvalidOperationException> (
				() => CoreWindowing.ComputeBaselineExitUtc (saturdayUtc, nyTz));
			}

		[Fact]
		public void FilterNyMorningOnly_RespectsDst_AndSkipsWeekends ()
			{
			var nyTz = CoreWindowing.NyTz;

			var candles = new List<Candle6h> ();

			// Зимний будний день (нет DST): утро = 07:00.
			var winterMorningLocal = new DateTime (2024, 1, 9, 7, 0, 0, DateTimeKind.Unspecified);
			Assert.False (nyTz.IsDaylightSavingTime (winterMorningLocal));
			var winterMorningUtc = TimeZoneInfo.ConvertTimeToUtc (winterMorningLocal, nyTz);

			candles.Add (new Candle6h
				{
				OpenTimeUtc = winterMorningUtc,
				Open = 100,
				High = 101,
				Low = 99,
				Close = 100.5
				});

			// Летний будний день (с DST): утро = 08:00.
			var summerMorningLocal = new DateTime (2024, 6, 10, 8, 0, 0, DateTimeKind.Unspecified);
			Assert.True (nyTz.IsDaylightSavingTime (summerMorningLocal));
			var summerMorningUtc = TimeZoneInfo.ConvertTimeToUtc (summerMorningLocal, nyTz);

			candles.Add (new Candle6h
				{
				OpenTimeUtc = summerMorningUtc,
				Open = 200,
				High = 202,
				Low = 198,
				Close = 201
				});

			// Выходной: суббота — не должен попасть.
			var weekendLocal = new DateTime (2024, 1, 6, 7, 0, 0, DateTimeKind.Unspecified);
			Assert.Equal (DayOfWeek.Saturday, weekendLocal.DayOfWeek);
			var weekendUtc = TimeZoneInfo.ConvertTimeToUtc (weekendLocal, nyTz);

			candles.Add (new Candle6h
				{
				OpenTimeUtc = weekendUtc,
				Open = 150,
				High = 151,
				Low = 149,
				Close = 150.5
				});

			var filtered = CoreWindowing.FilterNyMorningOnly (candles, nyTz);

			// Ожидаем ровно 2 будних утренних окна (зима/лето), выходной — выкинут.
			Assert.Equal (2, filtered.Count);
			Assert.Contains (filtered, c => c.OpenTimeUtc == winterMorningUtc);
			Assert.Contains (filtered, c => c.OpenTimeUtc == summerMorningUtc);
			Assert.DoesNotContain (filtered, c => c.OpenTimeUtc == weekendUtc);
			}

		[Fact]
		public void IsNyMorning_True_OnlyForMorningBar ()
			{
			var nyTz = CoreWindowing.NyTz;

			// Будний день зимой: утро 07:00, день 13:00.
			var morningLocal = new DateTime (2024, 1, 10, 7, 0, 0, DateTimeKind.Unspecified);
			var dayLocal = new DateTime (2024, 1, 10, 13, 0, 0, DateTimeKind.Unspecified);

			var morningUtc = TimeZoneInfo.ConvertTimeToUtc (morningLocal, nyTz);
			var dayUtc = TimeZoneInfo.ConvertTimeToUtc (dayLocal, nyTz);

			Assert.True (CoreWindowing.IsNyMorning (morningUtc, nyTz));
			Assert.False (CoreWindowing.IsNyMorning (dayUtc, nyTz));

			// Выходной — всегда false, даже если час совпадает.
			var weekendLocal = new DateTime (2024, 1, 13, 7, 0, 0, DateTimeKind.Unspecified); // суббота
			Assert.Equal (DayOfWeek.Saturday, weekendLocal.DayOfWeek);
			var weekendUtc = TimeZoneInfo.ConvertTimeToUtc (weekendLocal, nyTz);

			Assert.False (CoreWindowing.IsNyMorning (weekendUtc, nyTz));
			}

		[Fact]
		public void BuildSpacedTest_TakesBlocksFromEnd_WithSkips_AndKeepsOrder ()
			{
			// Генерим простой ряд дат.
			var rows = new List<DataRow> ();
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 10; i++)
				{
				rows.Add (new DataRow
					{
					Date = start.AddDays (i),
					Label = i
					});
				}

			// Берём 2 блока по 3 дня с конца, между блоками пропуск 2 дня.
			// Дни: [7,8,9] и [2,3,4] (5,6 — скип).
			var spaced = CoreWindowing.BuildSpacedTest (rows, take: 3, skip: 2, blocks: 2);

			Assert.Equal (6, spaced.Count);

			// Проверяем отсортированность по дате.
			var dates = spaced.Select (r => r.Date).ToList ();
			Assert.True (dates.SequenceEqual (dates.OrderBy (d => d)));

			// И конкретный набор label'ов (по нашей генерации).
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
				rows.Add (new DataRow
					{
					Date = start.AddDays (i),
					Label = i
					});
				}

			// Группируем по блокам по 4 ряда:
			// ожидаем 3 блока: [0..3], [4..7], [8..9].
			var blocks = CoreWindowing.GroupByBlocks (rows, blockSize: 4).ToList ();

			Assert.Equal (3, blocks.Count);

			Assert.Equal (new[] { 0, 1, 2, 3 }, blocks[0].Select (r => r.Label));
			Assert.Equal (new[] { 4, 5, 6, 7 }, blocks[1].Select (r => r.Label));
			Assert.Equal (new[] { 8, 9 }, blocks[2].Select (r => r.Label));
			}
		}
	}
