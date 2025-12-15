using System;
using System.Collections.Generic;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.Causal
	{
	/// <summary>
	/// Контракт каузального Windowing и TrainBoundary:
	/// - baseline-exit не определён для weekend-entry (должно падать в Windowing и "false" в TryGetBaselineExitUtc);
	/// - для Mon-Thu exit = следующее утро 08:00 NY - 2 минуты (то есть 07:58 NY);
	/// - для Friday exit уходит на следующий рабочий день после уикенда;
	/// - TrainBoundary.Split обязан относить weekend в Excluded.
	/// </summary>
	public sealed class WindowingAndTrainBoundaryTests
		{
		[Fact]
		public void ComputeBaselineExitUtc_Throws_ForWeekendEntry ()
			{
			// 2020-02-22 — суббота. Берём UTC-момент так, чтобы в NY это тоже была суббота.
			var entryUtc = new DateTime (2020, 2, 22, 15, 0, 0, DateTimeKind.Utc);

			var ex = Assert.Throws<InvalidOperationException> (
				() => Windowing.ComputeBaselineExitUtc (entryUtc));

			Assert.Contains ("weekend entry", ex.Message);
			}

		[Fact]
		public void ComputeBaselineExitUtc_ForWeekday_EndsAt_0758_NyLocal ()
			{
			// Понедельник.
			var entryUtc = new DateTime (2020, 2, 24, 15, 0, 0, DateTimeKind.Utc);

			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc);

			Assert.True (exitUtc > entryUtc);

			var exitNy = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, Windowing.NyTz);

			// По контракту это 08:00 - 2 минуты => 07:58 NY.
			Assert.Equal (7, exitNy.Hour);
			Assert.Equal (58, exitNy.Minute);

			// Это должно быть "следующее утро" в смысле NY-календарного дня.
			// Сравниваем локальные даты (оба DateTime здесь Unspecified и принадлежат NY-контексту).
			var entryNy = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, Windowing.NyTz);
			Assert.True (exitNy.Date >= entryNy.Date.AddDays (1));
			}

		[Fact]
		public void ComputeBaselineExitUtc_ForFriday_GoesToNextBusinessMorning ()
			{
			// 2020-02-28 — пятница.
			var entryUtc = new DateTime (2020, 2, 28, 15, 0, 0, DateTimeKind.Utc);

			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc);

			var ny = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, Windowing.NyTz);

			// Следующий рабочий день после уикенда — понедельник.
			Assert.Equal (DayOfWeek.Monday, ny.DayOfWeek);

			// 07:58 NY по контракту.
			Assert.Equal (7, ny.Hour);
			Assert.Equal (58, ny.Minute);
			}

		[Fact]
		public void TrainBoundary_TryGetBaselineExitUtc_ReturnsFalse_ForWeekend ()
			{
			var boundary = new TrainBoundary (
				trainUntilUtc: new DateTime (2020, 3, 10, 0, 0, 0, DateTimeKind.Utc),
				nyTz: Windowing.NyTz);

			var weekendEntryUtc = new DateTime (2020, 2, 22, 15, 0, 0, DateTimeKind.Utc);

			bool ok = boundary.TryGetBaselineExitUtc (weekendEntryUtc, out var exitUtc);

			Assert.False (ok);
			Assert.Equal (default, exitUtc);
			}

		[Fact]
		public void TrainBoundary_IsTrainEntry_UsesBaselineExit_NotEntryDate ()
			{
			var entryUtc = new DateTime (2020, 2, 24, 15, 0, 0, DateTimeKind.Utc);
			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc);

			var boundaryAtExit = new TrainBoundary (trainUntilUtc: exitUtc, nyTz: Windowing.NyTz);
			Assert.True (boundaryAtExit.IsTrainEntry (entryUtc));
			Assert.False (boundaryAtExit.IsOosEntry (entryUtc));

			// Сдвигаем trainUntil на тик назад: тот же entry должен стать OOS.
			var boundaryBeforeExit = new TrainBoundary (trainUntilUtc: exitUtc.AddTicks (-1), nyTz: Windowing.NyTz);
			Assert.False (boundaryBeforeExit.IsTrainEntry (entryUtc));
			Assert.True (boundaryBeforeExit.IsOosEntry (entryUtc));
			}

		[Fact]
		public void TrainBoundary_Split_PutsWeekendsIntoExcluded ()
			{
			// trainUntil выберем далеко вперёд, чтобы все будни попали в train.
			var boundary = new TrainBoundary (
				trainUntilUtc: new DateTime (2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				nyTz: Windowing.NyTz);

			var items = new List<DateTime>
				{
				// Saturday
				new DateTime (2020, 2, 22, 15, 0, 0, DateTimeKind.Utc),
				// Monday
				new DateTime (2020, 2, 24, 15, 0, 0, DateTimeKind.Utc),
				// Sunday
				new DateTime (2020, 2, 23, 15, 0, 0, DateTimeKind.Utc),
				// Tuesday
				new DateTime (2020, 2, 25, 15, 0, 0, DateTimeKind.Utc),
				};

			var split = boundary.Split (items, x => x);

			Assert.Equal (2, split.Train.Count);
			Assert.Empty (split.Oos);
			Assert.Equal (2, split.Excluded.Count);
			}
		}
	}
