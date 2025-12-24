using System;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Tests.Causal
	{
	/// <summary>
	/// Контракт NY-окон:
	/// - weekend-entry запрещён;
	/// - IsNyMorning: 07:00 зимой / 08:00 летом;
	/// - baseline-exit = следующее утро (07/08) - 2 минуты (06:58/07:58);
	/// - Friday переносится на ближайший рабочий день после уикенда;
	/// - TrainBoundary опирается на baseline-exit, а weekend кладёт в Excluded.
	/// </summary>
	public sealed class NyWindowingContractTests
		{
		[Fact]
		public void ComputeBaselineExitUtc_Throws_ForWeekendEntry ()
			{
			// Суббота в NY: берём 12:00 UTC (обычно попадает в утренний слот, но день выходной).
			var entryUtc = new DateTime (2024, 1, 6, 12, 0, 0, DateTimeKind.Utc);

			var ex = Assert.Throws<InvalidOperationException> (() => NyWindowing.ComputeBaselineExitUtc (entryUtc));
			Assert.Contains ("Weekend entry", ex.Message, StringComparison.OrdinalIgnoreCase);
			}

		[Fact]
		public void TryComputeBaselineExitUtc_ReturnsFalse_ForWeekendEntry ()
			{
			var entryUtc = new DateTime (2024, 1, 6, 12, 0, 0, DateTimeKind.Utc);

			bool ok = NyWindowing.TryComputeBaselineExitUtc (entryUtc, NyWindowing.NyTz, out var exitUtc);

			Assert.False (ok);
			Assert.Equal (default, exitUtc);
			}

		[Fact]
		public void WinterWeekday_ExitIs_0658_NyLocal_NextDay ()
			{
			// Зима: 12:00 UTC == 07:00 NY.
			var entryUtc = new DateTime (2024, 1, 8, 12, 0, 0, DateTimeKind.Utc); // Monday
			Assert.True (NyWindowing.IsNyMorning (entryUtc));

			var exitUtc = NyWindowing.ComputeBaselineExitUtc (entryUtc);
			Assert.True (exitUtc > entryUtc);

			var nyExit = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, NyWindowing.NyTz);

			Assert.Equal (6, nyExit.Hour);
			Assert.Equal (58, nyExit.Minute);

			var nyEntry = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, NyWindowing.NyTz);
			Assert.Equal (nyEntry.Date.AddDays (1), nyExit.Date);
			}

		[Fact]
		public void SummerWeekday_ExitIs_0758_NyLocal_NextDay ()
			{
			// Лето: 12:00 UTC == 08:00 NY.
			var entryUtc = new DateTime (2024, 6, 10, 12, 0, 0, DateTimeKind.Utc); // Monday
			Assert.True (NyWindowing.IsNyMorning (entryUtc));

			var exitUtc = NyWindowing.ComputeBaselineExitUtc (entryUtc);
			var nyExit = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, NyWindowing.NyTz);

			Assert.Equal (7, nyExit.Hour);
			Assert.Equal (58, nyExit.Minute);

			var nyEntry = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, NyWindowing.NyTz);
			Assert.Equal (nyEntry.Date.AddDays (1), nyExit.Date);
			}

		[Fact]
		public void Friday_GoesToNextBusinessMorning ()
			{
			// Пятница зимой: 12:00 UTC == 07:00 NY.
			var entryUtc = new DateTime (2024, 1, 5, 12, 0, 0, DateTimeKind.Utc); // Friday
			var exitUtc = NyWindowing.ComputeBaselineExitUtc (entryUtc);
			var nyExit = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, NyWindowing.NyTz);

			Assert.Equal (DayOfWeek.Monday, nyExit.DayOfWeek);
			Assert.Equal (6, nyExit.Hour);   // зима => 07:00 - 2m = 06:58
			Assert.Equal (58, nyExit.Minute);
			}

		[Fact]
		public void TrainBoundary_UsesBaselineExit_AndWeekendsGoToExcluded ()
			{
			// Берём границу далеко вперёд, чтобы все будни попали в train.
			var boundary = new TrainBoundary (
				trainUntilUtc: new DateTime (2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				nyTz: NyWindowing.NyTz);

			var items = new[]
			{
				new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc), // Sat
				new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc), // Mon
				new DateTime(2024, 1, 7, 12, 0, 0, DateTimeKind.Utc), // Sun
				new DateTime(2024, 1, 9, 12, 0, 0, DateTimeKind.Utc), // Tue
			};

			var split = boundary.Split (items, x => x);

			Assert.Equal (2, split.Train.Count);
			Assert.Empty (split.Oos);
			Assert.Equal (2, split.Excluded.Count);
			}
		}
	}
