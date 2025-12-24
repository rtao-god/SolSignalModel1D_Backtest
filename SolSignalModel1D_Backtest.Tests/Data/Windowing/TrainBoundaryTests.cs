using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Data.NyWindowing
{
    public sealed class TrainBoundaryTests
    {
        [Fact]
        public void SplitByTrainUntilUtc_UsesBaselineExitUtc_WeekendExcluded()
        {
            var tz = NyNyWindowingTestUtils.NewYorkTz;

            var entries = new List<EntryUtc>
                {
                NyNyWindowingTestUtils.EntryUtcFromNyDayOrThrow (2025, 1, 2), // Thu
				NyNyWindowingTestUtils.EntryUtcFromNyDayOrThrow (2025, 1, 3), // Fri
				NyNyWindowingTestUtils.EntryUtcFromUtcOrThrow (new DateTime (2025, 1, 4, 12, 0, 0, DateTimeKind.Utc)), // Sat local
				};

            // Cutoff = baseline-exit для Thu (Fri 11:58Z в этом примере утилит)
            var trainUntilUtc = new TrainUntilUtc(new DateTime(2025, 1, 3, 11, 58, 0, DateTimeKind.Utc));

            var split = TrainSplitByBaselineExit.Split(
                items: entries,
                entrySelector: e => e,
                trainUntilUtc: trainUntilUtc,
                nyTz: tz);

            Assert.Single(split.Train);
            Assert.Single(split.Oos);
            Assert.Single(split.Excluded);

            Assert.Equal(new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc), split.Train[0].Value);
            Assert.Equal(new DateTime(2025, 1, 3, 12, 0, 0, DateTimeKind.Utc), split.Oos[0].Value);
        }
    }
}
