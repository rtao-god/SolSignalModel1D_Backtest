using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
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
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromUtcOrThrow(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

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

        [Fact]
        public void SplitByBaselineExitStrict_Throws_WhenExcludedExists()
        {
            var nyTz = TimeZones.NewYork;
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromUtcOrThrow(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var items = new List<EntryUtc>
            {
                new EntryUtc(new UtcInstant(new DateTime(2020, 2, 22, 15, 0, 0, DateTimeKind.Utc))), // Sat (NY)
				new EntryUtc(new UtcInstant(new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc)))  // Mon
			};

            items.Sort((a, b) => a.Value.CompareTo(b.Value));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                NyTrainSplit.SplitByBaselineExitStrict(
                    ordered: items,
                    entrySelector: e => e,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: nyTz,
                    tag: "strict-split"));

            Assert.Contains("strict-split", ex.Message);
            Assert.Contains("кол-во", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SplitByBaselineExitStrict_ReturnsTrainOnly_WithTag()
        {
            var nyTz = TimeZones.NewYork;

            var entryTrain = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc))); // Mon
            var entryOos = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 25, 15, 0, 0, DateTimeKind.Utc)));   // Tue

            var exitTrain = NyWindowing.ComputeBaselineExitUtc(entryTrain, nyTz);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitTrain.Value);

            var items = new List<EntryUtc> { entryTrain, entryOos };

            var split = NyTrainSplit.SplitByBaselineExitStrict(
                ordered: items,
                entrySelector: e => e,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz,
                tag: "strict-split-ok");

            Assert.Single(split.Train);
            Assert.Single(split.Oos);
            Assert.Equal("strict-split-ok", split.Train.Tag);
            Assert.Equal(trainUntilExitDayKeyUtc.Value, split.Train.TrainUntilExitDayKeyUtc.Value);
            Assert.Equal(entryTrain.Value, split.Train[0].Value);
            Assert.Equal(entryOos.Value, split.Oos[0].Value);
        }

        [Fact]
        public void SplitByBaselineExit_RespectsBoundaryAndOrder()
        {
            var nyTz = TimeZones.NewYork;

            var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(2024, 1, 1, 0),
                count: 12,
                hour: 7);

            var items = new List<EntryUtc>(datesUtc.Count);
            for (int i = 0; i < datesUtc.Count; i++)
                items.Add(new EntryUtc(datesUtc[i]));

            var exit = NyWindowing.ComputeBaselineExitUtc(items[5], nyTz);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(exit.Value);

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: items,
                entrySelector: e => e,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz);

            Assert.True(split.Train.Count > 0);
            Assert.True(split.Oos.Count > 0);
            Assert.Empty(split.Excluded);

            var lastTrainExit = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(
                NyWindowing.ComputeBaselineExitUtc(split.Train[^1], nyTz).Value);
            var firstOosExit = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(
                NyWindowing.ComputeBaselineExitUtc(split.Oos[0], nyTz).Value);

            Assert.True(lastTrainExit.Value <= trainUntilExitDayKeyUtc.Value);
            Assert.True(firstOosExit.Value > trainUntilExitDayKeyUtc.Value);

            var trainSet = new HashSet<DateTime>();
            for (int i = 0; i < split.Train.Count; i++)
                trainSet.Add(split.Train[i].Value);

            for (int i = 0; i < split.Oos.Count; i++)
                Assert.DoesNotContain(split.Oos[i].Value, trainSet);

            for (int i = 0; i < split.Train.Count; i++)
            {
                var exitDay = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(
                    NyWindowing.ComputeBaselineExitUtc(split.Train[i], nyTz).Value);
                Assert.True(exitDay.Value <= trainUntilExitDayKeyUtc.Value);
            }

            for (int i = 0; i < split.Oos.Count; i++)
            {
                var exitDay = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(
                    NyWindowing.ComputeBaselineExitUtc(split.Oos[i], nyTz).Value);
                Assert.True(exitDay.Value > trainUntilExitDayKeyUtc.Value);
            }
        }

        [Fact]
        public void SplitByBaselineExit_HandlesDstSpringForwardBoundary()
        {
            var nyTz = TimeZones.NewYork;

            var entryFriLocal = new DateTime(2024, 3, 8, 7, 0, 0, DateTimeKind.Unspecified);
            var entryMonLocal = new DateTime(2024, 3, 11, 8, 0, 0, DateTimeKind.Unspecified);

            var entryFri = new EntryUtc(new UtcInstant(TimeZoneInfo.ConvertTimeToUtc(entryFriLocal, nyTz)));
            var entryMon = new EntryUtc(new UtcInstant(TimeZoneInfo.ConvertTimeToUtc(entryMonLocal, nyTz)));

            var exitFri = NyWindowing.ComputeBaselineExitUtc(entryFri, nyTz);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitFri.Value);

            var items = new List<EntryUtc> { entryFri, entryMon };
            items.Sort((a, b) => a.Value.CompareTo(b.Value));

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: items,
                entrySelector: e => e,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz);

            Assert.Single(split.Train);
            Assert.Single(split.Oos);
            Assert.Equal(entryFri.Value, split.Train[0].Value);
            Assert.Equal(entryMon.Value, split.Oos[0].Value);

            var oosExitDayKey = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(
                NyWindowing.ComputeBaselineExitUtc(split.Oos[0], nyTz).Value);

            Assert.True(oosExitDayKey.Value > trainUntilExitDayKeyUtc.Value);
        }

        [Fact]
        public void SplitByBaselineExit_Throws_WhenOrderedNotAscending()
        {
            var nyTz = TimeZones.NewYork;
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromUtcOrThrow(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var later = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 25, 15, 0, 0, DateTimeKind.Utc)));
            var earlier = new EntryUtc(new UtcInstant(new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc)));

            var items = new List<EntryUtc> { later, earlier };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                NyTrainSplit.SplitByBaselineExit(
                    ordered: items,
                    entrySelector: e => e,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: nyTz));

            Assert.Contains("ordered must be strictly ascending", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

