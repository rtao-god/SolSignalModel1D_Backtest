using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.ML.Daily
{
    public sealed class DailyDatasetBuilderLeakageTests
    {
        [Fact]
        public void Build_CutsTrainRowsByBaselineExit_NotByEntryUtc()
        {
            var nyTz = NyWindowing.NyTz;

            var entriesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(2025, 1, 3, 0),
                count: 2,
                hour: 8);

            var entryUtcRaw = entriesUtc[1];
            var entryUtc = new EntryUtc(entryUtcRaw);

            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, nyTz);
            Assert.True(exitUtc.Value > entryUtc.Value);

            var rows = new List<LabeledCausalRow>
            {
                CreateRow(dateUtc: entriesUtc[0], label: 1, regimeDown: false, nyTz: nyTz),
                CreateRow(dateUtc: entryUtc.Value, label: 2, regimeDown: false, nyTz: nyTz)
            };

            var exitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value);

            var trainUntilBeforeExit = TrainUntilExitDayKeyUtc.FromExitDayKeyUtc(
                ExitDayKeyUtc.FromUtcOrThrow(exitDayKeyUtc.Value.AddDays(-1)));

            var dsBeforeExit = DailyDatasetBuilder.Build(rows, trainUntilBeforeExit, false, false, 0.5, null);
            Assert.DoesNotContain(dsBeforeExit.TrainRows, r => r.Causal.EntryUtc.Value == entryUtc.Value);
            Assert.Contains(dsBeforeExit.TrainRows, r => r.Causal.EntryUtc.Value == entriesUtc[0]);

            var trainUntilAtExit = TrainUntilExitDayKeyUtc.FromExitDayKeyUtc(exitDayKeyUtc);
            var dsAtExit = DailyDatasetBuilder.Build(rows, trainUntilAtExit, false, false, 0.5, null);

            Assert.Contains(dsAtExit.TrainRows, r => r.Causal.EntryUtc.Value == entryUtc.Value);
        }

        [Fact]
        public void Build_AllTrainListsContainOnlyTrainEntries_ByExitDayKeyBoundary()
        {
            var nyTz = NyWindowing.NyTz;

            var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(2025, 1, 1, 0),
                count: 120,
                hour: 8);

            var rows = new List<LabeledCausalRow>(datesUtc.Count);
            for (int i = 0; i < datesUtc.Count; i++)
            {
                rows.Add(CreateRow(
                    dateUtc: datesUtc[i],
                    label: i % 3,
                    regimeDown: (i % 5 == 0),
                    nyTz: nyTz));
            }

            var pivotEntryRaw = datesUtc[^20];
            var pivotEntry = new EntryUtc(pivotEntryRaw);
            var pivotExit = NyWindowing.ComputeBaselineExitUtc(pivotEntry, nyTz);

            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(pivotExit.Value);

            var ds = DailyDatasetBuilder.Build(rows, trainUntilExitDayKeyUtc, false, false, 0.5, null);

            Assert.NotEmpty(ds.TrainRows);

            static void AssertAllTrain(IEnumerable<LabeledCausalRow> xs, TrainUntilExitDayKeyUtc boundary, TimeZoneInfo nyTz, string tag)
            {
                foreach (var r in xs)
                {
                    var cls = NyTrainSplit.ClassifyByBaselineExit(
                        entryUtc: r.Causal.EntryUtc,
                        trainUntilExitDayKeyUtc: boundary,
                        nyTz: nyTz,
                        baselineExitDayKeyUtc: out _);

                    Assert.True(
                        cls == NyTrainSplit.EntryClass.Train,
                        $"{tag} contains non-train entry by exit-day-key boundary: {r.Causal.EntryUtc.Value:O}");
                }
            }

            AssertAllTrain(ds.TrainRows, trainUntilExitDayKeyUtc, nyTz, nameof(ds.TrainRows));
            AssertAllTrain(ds.MoveTrainRows, trainUntilExitDayKeyUtc, nyTz, nameof(ds.MoveTrainRows));
            AssertAllTrain(ds.DirNormalRows, trainUntilExitDayKeyUtc, nyTz, nameof(ds.DirNormalRows));
            AssertAllTrain(ds.DirDownRows, trainUntilExitDayKeyUtc, nyTz, nameof(ds.DirDownRows));
        }

        private static LabeledCausalRow CreateRow(DateTime dateUtc, int label, bool regimeDown, TimeZoneInfo nyTz)
        {
            if (dateUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("dateUtc must be UTC.", nameof(dateUtc));

            double solRet1 = label == 2 ? 0.02 : label == 0 ? -0.02 : 0.0;

            var entry = new EntryUtc(dateUtc);
            if (!NyWindowing.TryCreateNyTradingEntryUtc(entry, nyTz, out var nyEntry))
                throw new InvalidOperationException($"[tests] expected NY trading entry, got weekend by NY calendar: {dateUtc:O}");

            var causal = new CausalDataRow(
                entryUtc: nyEntry,
                regimeDown: regimeDown,
                isMorning: true,
                hardRegime: 1,
                minMove: 0.02,

                solRet30: solRet1 * 0.2,
                btcRet30: 0.0,
                solBtcRet30: 0.0,

                solRet1: solRet1,
                solRet3: solRet1 * 0.7,
                btcRet1: solRet1 * 0.1,
                btcRet3: solRet1 * 0.05,

                fngNorm: 0.0,
                dxyChg30: 0.0,
                goldChg30: 0.0,

                btcVs200: 0.0,

                solRsiCenteredScaled: 0.0,
                rsiSlope3Scaled: 0.0,

                gapBtcSol1: 0.0,
                gapBtcSol3: 0.0,

                atrPct: 0.02,
                dynVol: 1.0,

                solAboveEma50: 1.0,
                solEma50vs200: 0.01,
                btcEma50vs200: 0.01);

            var microTruth = label == 1
                ? OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.MicroNeutral)
                : OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.NonFlatTruth);

            return new LabeledCausalRow(
                causal: causal,
                trueLabel: label,
                microTruth: microTruth);
        }
    }
}
