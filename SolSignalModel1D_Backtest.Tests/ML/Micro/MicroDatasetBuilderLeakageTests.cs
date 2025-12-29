using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Micro
{
    /// <summary>
    /// Инварианты микро-датасета:
    /// 1) Train/Micro-выборки не должны включать дни позже trainUntil (в терминах day-key);
    /// 2) MicroRows должна быть подмножеством TrainRows.
    /// </summary>
    public sealed class MicroDatasetBuilderLeakageTests
    {
        [Fact]
        public void Build_UsesOnlyRowsUpToTrainUntil()
        {
            var entriesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(2025, 1, 1, 0),
                count: 50,
                hour: 8);

            var rows = new List<LabeledCausalRow>(50);
            for (int i = 0; i < 50; i++)
            {
                var entryUtc = entriesUtc[i];

                var causal = MakeCausalRow(
                    dateUtc: entryUtc,
                    isMorning: true,
                    regimeDown: (i % 5 == 0),
                    hardRegime: i % 3,
                    minMove: 0.02,
                    seed: i);

                bool factUp = (i % 3 == 0);
                bool factDown = (i % 3 == 1);
                int trueLabel = i % 3;

                var microTruth = trueLabel == 1
                    ? (factUp
                        ? OptionalValue<MicroTruthDirection>.Present(MicroTruthDirection.Up)
                        : factDown
                            ? OptionalValue<MicroTruthDirection>.Present(MicroTruthDirection.Down)
                            : OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.MicroNeutral))
                    : OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.NonFlatTruth);

                rows.Add(new LabeledCausalRow(
                    causal: causal,
                    trueLabel: trueLabel,
                    microTruth: microTruth));
            }

            var t = entriesUtc[30];
            var trainUntil = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromUtcOrThrow(trainUntil);

            var ds = MicroDatasetBuilder.Build(rows, trainUntilExitDayKeyUtc);

            Assert.NotEmpty(ds.TrainRows);
            Assert.NotEmpty(ds.MicroRows);

            Assert.All(ds.TrainRows, r => Assert.True(CausalTimeKey.EntryDayKeyUtc(r).Value <= trainUntilExitDayKeyUtc.Value));
            Assert.All(ds.MicroRows, r => Assert.True(CausalTimeKey.EntryDayKeyUtc(r).Value <= trainUntilExitDayKeyUtc.Value));
        }

        [Fact]
        public void Build_MicroRowsAreSubsetOfTrainRows()
        {
            var entriesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(2025, 1, 1, 0),
                count: 50,
                hour: 8);

            var rows = new List<LabeledCausalRow>(50);
            for (int i = 0; i < 50; i++)
            {
                var entryUtc = entriesUtc[i];

                var causal = MakeCausalRow(
                    dateUtc: entryUtc,
                    isMorning: true,
                    regimeDown: (i % 7 == 0),
                    hardRegime: i % 3,
                    minMove: 0.02,
                    seed: i);

                bool factUp = (i % 4 == 0);
                bool factDown = (i % 4 == 1);
                int trueLabel = i % 3;

                var microTruth = trueLabel == 1
                    ? (factUp
                        ? OptionalValue<MicroTruthDirection>.Present(MicroTruthDirection.Up)
                        : factDown
                            ? OptionalValue<MicroTruthDirection>.Present(MicroTruthDirection.Down)
                            : OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.MicroNeutral))
                    : OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.NonFlatTruth);

                rows.Add(new LabeledCausalRow(
                    causal: causal,
                    trueLabel: trueLabel,
                    microTruth: microTruth));
            }

            var t = entriesUtc[40];
            var trainUntil = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromUtcOrThrow(trainUntil);

            var ds = MicroDatasetBuilder.Build(rows, trainUntilExitDayKeyUtc);

            var trainKeys = ds.TrainRows
                .Select(r => CausalTimeKey.EntryDayKeyUtc(r))
                .ToHashSet();

            foreach (var microRow in ds.MicroRows)
                Assert.Contains(CausalTimeKey.EntryDayKeyUtc(microRow), trainKeys);
        }

        private static CausalDataRow MakeCausalRow(
            DateTime dateUtc,
            bool isMorning,
            bool regimeDown,
            int hardRegime,
            double minMove,
            int seed)
        {
            double s = seed;

            var nyEntry = NyWindowing.CreateNyTradingEntryUtcOrThrow(new EntryUtc(dateUtc), NyWindowing.NyTz);

            return new CausalDataRow(
                entryUtc: nyEntry,
                regimeDown: regimeDown,
                isMorning: isMorning,
                hardRegime: hardRegime,
                minMove: minMove,

                solRet30: 0.001 * s,
                btcRet30: 0.0005 * s,
                solBtcRet30: 0.001 * s - 0.0005 * s,

                solRet1: 0.0001 * s,
                solRet3: 0.0003 * s,
                btcRet1: 0.00005 * s,
                btcRet3: 0.00015 * s,

                fngNorm: 0.2,
                dxyChg30: 0.0,
                goldChg30: 0.0,

                btcVs200: 0.1,

                solRsiCenteredScaled: 0.0,
                rsiSlope3Scaled: 0.0,

                gapBtcSol1: 0.0,
                gapBtcSol3: 0.0,

                atrPct: 0.02,
                dynVol: 0.015,

                solAboveEma50: 1.0,
                solEma50vs200: 0.1,
                btcEma50vs200: 0.1);
        }
    }
}

