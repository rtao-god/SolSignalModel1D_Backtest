using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;
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
            var startEntryUtc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var rows = new List<LabeledCausalRow>(50);
            for (int i = 0; i < 50; i++)
            {
                var entryUtc = startEntryUtc.AddDays(i);

                var causal = MakeCausalRow(
                    dateUtc: entryUtc,
                    isMorning: true,
                    regimeDown: (i % 5 == 0),
                    hardRegime: i % 3,
                    minMove: 0.02,
                    seed: i);

                bool factUp = (i % 3 == 0);
                bool factDown = (i % 3 == 1);

                rows.Add(new LabeledCausalRow(
                    causal: causal,
                    trueLabel: i % 3,
                    factMicroUp: factUp,
                    factMicroDown: factDown));
            }

            var t = startEntryUtc.AddDays(30);
            var trainUntil = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);

            var ds = MicroDatasetBuilder.Build(rows, trainUntil);

            Assert.NotEmpty(ds.TrainRows);
            Assert.NotEmpty(ds.MicroRows);

            Assert.All(ds.TrainRows, r => Assert.True(CausalTimeKey.DayKeyUtc(r).Value <= trainUntil));
            Assert.All(ds.MicroRows, r => Assert.True(CausalTimeKey.DayKeyUtc(r).Value <= trainUntil));
        }

        [Fact]
        public void Build_MicroRowsAreSubsetOfTrainRows()
        {
            var startEntryUtc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var rows = new List<LabeledCausalRow>(50);
            for (int i = 0; i < 50; i++)
            {
                var entryUtc = startEntryUtc.AddDays(i);

                var causal = MakeCausalRow(
                    dateUtc: entryUtc,
                    isMorning: true,
                    regimeDown: (i % 7 == 0),
                    hardRegime: i % 3,
                    minMove: 0.02,
                    seed: i);

                bool factUp = (i % 4 == 0);
                bool factDown = (i % 4 == 1);

                rows.Add(new LabeledCausalRow(
                    causal: causal,
                    trueLabel: i % 3,
                    factMicroUp: factUp,
                    factMicroDown: factDown));
            }

            var t = startEntryUtc.AddDays(40);
            var trainUntil = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);

            var ds = MicroDatasetBuilder.Build(rows, trainUntil);

            var trainKeys = ds.TrainRows
                .Select(r => CausalTimeKey.DayKeyUtc(r))
                .ToHashSet();

            foreach (var microRow in ds.MicroRows)
                Assert.Contains(CausalTimeKey.DayKeyUtc(microRow), trainKeys);
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

            return new CausalDataRow(
                entryUtc: new EntryUtc(dateUtc),
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
