using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.Tests.Data.NyWindowing
{
    public sealed class TrainBoundaryTests
    {
        [Fact]
        public void SplitByBaselineExit_UsesBaselineExitDayKey_WeekendExcluded()
        {
            var tz = NyWindowingTestUtils.NewYorkTz;

            var entries = new List<EntryUtc>
            {
                NyWindowingTestUtils.EntryUtcFromNyDayOrThrow(2025, 1, 2), // Thu
				NyWindowingTestUtils.EntryUtcFromNyDayOrThrow(2025, 1, 3), // Fri
				NyWindowingTestUtils.EntryUtcFromUtcOrThrow(new DateTime(2025, 1, 4, 12, 0, 0, DateTimeKind.Utc)), // Sat local
			};  

            var exitThu = CoreNyWindowing.ComputeBaselineExitUtc(entries[0], tz);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitThu.Value);

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: entries,
                entrySelector: e => e,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: tz);

            Assert.Single(split.Train);
            Assert.Single(split.Oos);
            Assert.Single(split.Excluded);

            Assert.Equal(entries[0].Value, split.Train[0].Value);
            Assert.Equal(entries[1].Value, split.Oos[0].Value);
        }
    }
}

