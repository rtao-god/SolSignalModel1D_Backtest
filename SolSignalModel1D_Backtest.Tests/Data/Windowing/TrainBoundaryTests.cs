using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing
	{
	public sealed class TrainBoundaryTests
		{
		[Fact]
		public void TryGetBaselineExitUtc_ReturnsFalse_OnWeekendEntry ()
			{
			var nyTz = CoreWindowing.NyTz;

			var boundary = new TrainBoundary (
				trainUntilUtc: new DateTime (2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
				nyTz: nyTz);

			var saturdayUtc = NyTestDates.ToUtc (NyTestDates.NyLocal (2025, 1, 4, 12, 0)); // суббота

			var ok = boundary.TryGetBaselineExitUtc (saturdayUtc, out var exitUtc);

			Assert.False (ok);
			Assert.Equal (default, exitUtc);

			// Важно: weekend entry не должен считаться ни train, ни OOS.
			Assert.False (boundary.IsTrainEntry (saturdayUtc));
			Assert.False (boundary.IsOosEntry (saturdayUtc));
			}

		[Fact]
		public void IsTrainEntry_DependsOnBaselineExit_NotOnEntryUtc ()
			{
			var nyTz = CoreWindowing.NyTz;

			// Понедельник NY 08:00 -> baseline-exit во вторник 07:58 local (UTC зависит от DST).
			var entryUtc = NyTestDates.ToUtc (NyTestDates.NyLocal (2025, 1, 6, 8, 0));
			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);

			// trainUntil строго между entry и exit -> entry выглядит “в прошлом”,
			// но baseline-окно ещё “в будущем”, значит это обязано быть OOS.
			var midTicks = entryUtc.Ticks + (exitUtc.Ticks - entryUtc.Ticks) / 2;
			var trainUntilBetween = new DateTime (midTicks, DateTimeKind.Utc);

			var boundary = new TrainBoundary (trainUntilBetween, nyTz);

			Assert.False (boundary.IsTrainEntry (entryUtc));
			Assert.True (boundary.IsOosEntry (entryUtc));
			}

		[Fact]
		public void Split_FillsExcluded_ForWeekend_AndNeverSilentlyAssignsIt ()
			{
			var nyTz = CoreWindowing.NyTz;

			var trainUntilUtc = new DateTime (2025, 1, 20, 0, 0, 0, DateTimeKind.Utc);
			var boundary = new TrainBoundary (trainUntilUtc, nyTz);

			var saturdayUtc = NyTestDates.ToUtc (NyTestDates.NyLocal (2025, 1, 4, 12, 0));
			var mondayUtc = NyTestDates.ToUtc (NyTestDates.NyLocal (2025, 1, 6, 8, 0));

			var items = new List<DateTime> { saturdayUtc, mondayUtc };

			var split = boundary.Split (items, x => x);

			Assert.Single (split.Excluded);
			Assert.Contains (saturdayUtc, split.Excluded);

			// monday не обязан быть train (зависит от exit), но точно не excluded
			Assert.DoesNotContain (mondayUtc, split.Excluded);
			}
		}
	}
