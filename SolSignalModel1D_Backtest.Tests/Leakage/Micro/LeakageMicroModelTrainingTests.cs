using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Micro
{
    public sealed class LeakageMicroModelTrainingTests
    {
        [Fact]
        public void MicroDataset_IsFutureBlind_ToOosTailMutation_ByTrainBoundary()
        {
            var nyTz = NyWindowing.NyTz;

            var datesUtc = BuildNyWeekdayEntriesUtc(
                startUtc: new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc),
                count: 260,
                nyTz: nyTz);

            var allRows = BuildSyntheticRows(datesUtc, nyTz);

            var pivotEntry = new EntryUtc(datesUtc[^40]);
            var pivotExit = NyWindowing.ComputeBaselineExitUtc(pivotEntry, nyTz);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(pivotExit.Value.AddMinutes(1));

            var rowsA = CloneRows(allRows);
            var rowsB = CloneRows(allRows);

            MutateOosTail(rowsB, trainUntilExitDayKeyUtc, nyTz);

            var dsA = MicroDatasetBuilder.Build(rowsA, trainUntilExitDayKeyUtc);
            var dsB = MicroDatasetBuilder.Build(rowsB, trainUntilExitDayKeyUtc);

            AssertRowsEqual(dsA.TrainRows, dsB.TrainRows);
            AssertRowsEqual(dsA.MicroRows, dsB.MicroRows);

            Assert.DoesNotContain(dsA.TrainRows, r =>
            {
                var ny = TimeZoneInfo.ConvertTimeFromUtc(r.EntryUtc.Value, nyTz);
                return ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            });
        }

        private static List<DateTime> BuildNyWeekdayEntriesUtc(DateTime startUtc, int count, TimeZoneInfo nyTz)
        {
            if (startUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("startUtc must be UTC.", nameof(startUtc));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "count must be > 0.");
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var res = new List<DateTime>(count);
            var dt = startUtc;

            while (res.Count < count)
            {
                var ny = TimeZoneInfo.ConvertTimeFromUtc(dt, nyTz);
                if (ny.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    res.Add(dt);

                dt = dt.AddDays(1);
            }

            return res;
        }

        private static List<LabeledCausalRow> BuildSyntheticRows(IReadOnlyList<DateTime> datesUtc, TimeZoneInfo nyTz)
        {
            var rows = new List<LabeledCausalRow>(datesUtc.Count);

            for (int i = 0; i < datesUtc.Count; i++)
            {
                bool isMicro = (i % 3 == 0);
                bool microUp = isMicro && (i % 6 == 0);
                bool microDown = isMicro && !microUp;

                int trueLabel = isMicro ? 1 : 2;

                if (!NyWindowing.TryCreateNyTradingEntryUtc(new EntryUtc(datesUtc[i]), nyTz, out var nyEntry))
                    throw new InvalidOperationException($"[test] expected NY trading entry, got weekend? t={datesUtc[i]:O}");

                rows.Add(MakeRow(
                    entryUtc: nyEntry,
                    idx: i,
                    trueLabel: trueLabel,
                    isMicro: isMicro,
                    microUp: microUp,
                    microDown: microDown,
                    mutated: false));
            }

            return rows;
        }

        private static List<LabeledCausalRow> CloneRows(List<LabeledCausalRow> src)
        {
            var res = new List<LabeledCausalRow>(src.Count);

            for (int i = 0; i < src.Count; i++)
            {
                var r = src[i];
                res.Add(new LabeledCausalRow(
                    causal: CloneCausal(r.Causal),
                    trueLabel: r.TrueLabel,
                    factMicroUp: r.FactMicroUp,
                    factMicroDown: r.FactMicroDown));
            }

            return res;
        }

        private static void MutateOosTail(List<LabeledCausalRow> rows, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc, TimeZoneInfo nyTz)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (!NyWindowing.TryComputeBaselineExitUtc(new EntryUtc(r.EntryUtc.Value), nyTz, out var exitUtc))
                    continue;

                var exitDayKey = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value);
                if (exitDayKey.Value <= trainUntilExitDayKeyUtc.Value)
                    continue;

                bool newUp;
                bool newDown;

                if (r.FactMicroUp == r.FactMicroDown)
                {
                    newUp = (i % 2 == 0);
                    newDown = !newUp;
                }
                else
                {
                    newUp = !r.FactMicroUp;
                    newDown = !r.FactMicroDown;
                }

                if (!NyWindowing.TryCreateNyTradingEntryUtc(new EntryUtc(r.EntryUtc.Value), nyTz, out var nyEntry))
                    throw new InvalidOperationException($"[test] entry unexpectedly not NY-trading. t={r.EntryUtc.Value:O}");

                rows[i] = MakeRow(
                    entryUtc: nyEntry,
                    idx: i,
                    trueLabel: r.TrueLabel,
                    isMicro: newUp || newDown,
                    microUp: newUp,
                    microDown: newDown,
                    mutated: true);
            }
        }

        private static void AssertRowsEqual(IReadOnlyList<LabeledCausalRow> xs, IReadOnlyList<LabeledCausalRow> ys)
        {
            Assert.Equal(xs.Count, ys.Count);

            for (int i = 0; i < xs.Count; i++)
            {
                var a = xs[i];
                var b = ys[i];

                Assert.Equal(a.EntryUtc.Value, b.EntryUtc.Value);
                Assert.Equal(a.TrueLabel, b.TrueLabel);
                Assert.Equal(a.FactMicroUp, b.FactMicroUp);
                Assert.Equal(a.FactMicroDown, b.FactMicroDown);

                var va = a.Causal.FeaturesVector.ToArray();
                var vb = b.Causal.FeaturesVector.ToArray();

                Assert.Equal(va.Length, vb.Length);
                for (int j = 0; j < va.Length; j++)
                    Assert.Equal(va[j], vb[j]);
            }
        }

        private static CausalDataRow CloneCausal(CausalDataRow c)
        {
            var nyEntry = NyWindowing.CreateNyTradingEntryUtcOrThrow(c.EntryUtc, NyWindowing.NyTz);

            return new CausalDataRow(
                entryUtc: nyEntry,
                regimeDown: c.RegimeDown,
                isMorning: c.IsMorning,
                hardRegime: c.HardRegime,
                minMove: c.MinMove,

                solRet30: c.SolRet30,
                btcRet30: c.BtcRet30,
                solBtcRet30: c.SolBtcRet30,

                solRet1: c.SolRet1,
                solRet3: c.SolRet3,
                btcRet1: c.BtcRet1,
                btcRet3: c.BtcRet3,

                fngNorm: c.FngNorm,
                dxyChg30: c.DxyChg30,
                goldChg30: c.GoldChg30,

                btcVs200: c.BtcVs200,

                solRsiCenteredScaled: c.SolRsiCenteredScaled,
                rsiSlope3Scaled: c.RsiSlope3Scaled,

                gapBtcSol1: c.GapBtcSol1,
                gapBtcSol3: c.GapBtcSol3,

                atrPct: c.AtrPct,
                dynVol: c.DynVol,

                solAboveEma50: c.SolAboveEma50,
                solEma50vs200: c.SolEma50vs200,
                btcEma50vs200: c.BtcEma50vs200);
        }

        private static LabeledCausalRow MakeRow(
            NyTradingEntryUtc entryUtc,
            int idx,
            int trueLabel,
            bool isMicro,
            bool microUp,
            bool microDown,
            bool mutated)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized.", nameof(entryUtc));
            if (microUp && microDown)
                throw new InvalidOperationException("microUp and microDown cannot be true одновременно.");
            if (!isMicro && (microUp || microDown))
                throw new InvalidOperationException("Non-micro day cannot have microUp/microDown flags.");

            double dir = microUp ? 2.0 : (microDown ? -2.0 : 0.0);
            double m = mutated ? 9999.0 : 1.0;

            var causal = new CausalDataRow(
                entryUtc: entryUtc,
                regimeDown: false,
                isMorning: true,
                hardRegime: 0,
                minMove: 0.03,

                solRet30: dir * m,
                btcRet30: 0.01 * (idx + 1) * m,
                solBtcRet30: 0.001 * (idx + 1) * m,

                solRet1: 0.002 * (idx + 1) * m,
                solRet3: 0.003 * (idx + 1) * m,
                btcRet1: 0.004 * (idx + 1) * m,
                btcRet3: 0.005 * (idx + 1) * m,

                fngNorm: 0.10 * m,
                dxyChg30: -0.02 * m,
                goldChg30: 0.01 * m,

                btcVs200: 0.2 * m,

                solRsiCenteredScaled: 0.3 * m,
                rsiSlope3Scaled: 0.4 * m,

                gapBtcSol1: 0.01 * m,
                gapBtcSol3: 0.02 * m,

                atrPct: 0.05 * m,
                dynVol: 0.06 * m,

                solAboveEma50: 1.0 * m,
                solEma50vs200: 0.1 * m,
                btcEma50vs200: 0.2 * m);

            return new LabeledCausalRow(
                causal: causal,
                trueLabel: trueLabel,
                factMicroUp: microUp,
                factMicroDown: microDown);
        }
    }
}
