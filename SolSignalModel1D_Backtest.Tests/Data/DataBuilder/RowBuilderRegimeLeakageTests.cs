using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using Xunit;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
{
    public sealed class RowBuilderRegimeLeakageTests
    {
        [Fact]
        public void RegimeDown_DoesNotChange_WhenAllFutureAfterEntryIsMutated()
        {
            var tz = CoreNyWindowing.NyTz;

            const int days = 260;

            BuildSyntheticMarket(
                days: days,
                out var solAll6h_A,
                out var btcAll6h,
                out var paxgAll6h,
                out var solWinTrain_A,
                out var btcWinTrain,
                out var paxgWinTrain,
                out var solAll1m_A,
                out var fngBase,
                out var dxyBase);

            CloneMarket(
                solAll6h_A,
                solAll1m_A,
                out var solAll6h_B,
                out var solAll1m_B);

            // Важно: train-списки должны быть отдельными списками, но с теми же таймстемпами.
            var solWinTrain_B = BuildDailyWinTrainFromAll6h(solAll6h_B);

            var resA = RowBuilder.BuildDailyRows(
                solWinTrain: solWinTrain_A,
                btcWinTrain: btcWinTrain,
                paxgWinTrain: paxgWinTrain,
                solAll6h: solAll6h_A,
                solAll1m: solAll1m_A,
                fngHistory: fngBase,
                dxySeries: dxyBase,
                extraDaily: null,
                nyTz: tz);

            var rowsA = resA.CausalRows
                .OrderBy(r => r.EntryUtc.Value)
                .ToList();

            Assert.NotEmpty(rowsA);

            var entryUtc = FindMidEntryUtc(solWinTrain_A, tz);
            Assert.Contains(rowsA, r => r.EntryUtc.Value == entryUtc);

            MutateAllFutureAfterEntry(
                solAll6h: solAll6h_B,
                solWinTrain: solWinTrain_B,
                solAll1m: solAll1m_B,
                entryUtc: entryUtc,
                sol6hFactor: 10.0,
                sol1mFactor: 10.0);

            var fngB = new Dictionary<DateTime, double>(fngBase);
            var dxyB = new Dictionary<DateTime, double>(dxyBase);

            var entryDay = entryUtc.ToCausalDateUtc();
            foreach (var k in fngB.Keys.ToList())
            {
                if (k.ToCausalDateUtc() > entryDay)
                {
                    fngB[k] = 99.0;
                    dxyB[k] = dxyB[k] + 50.0;
                }
            }

            var resB = RowBuilder.BuildDailyRows(
                solWinTrain: solWinTrain_B,
                btcWinTrain: btcWinTrain,
                paxgWinTrain: paxgWinTrain,
                solAll6h: solAll6h_B,
                solAll1m: solAll1m_B,
                fngHistory: fngB,
                dxySeries: dxyB,
                extraDaily: null,
                nyTz: tz);

            var rowsB = resB.CausalRows
                .OrderBy(r => r.EntryUtc.Value)
                .ToList();

            Assert.NotEmpty(rowsB);

            var dictA = rowsA.ToDictionary(r => r.EntryUtc.Value, r => r.RegimeDown);
            var dictB = rowsB.ToDictionary(r => r.EntryUtc.Value, r => r.RegimeDown);

            foreach (var kv in dictA)
            {
                var dateUtc = kv.Key;
                if (dateUtc > entryUtc)
                    continue;

                Assert.True(dictB.ContainsKey(dateUtc), $"Во втором наборе нет строки для {dateUtc:O}");
                Assert.Equal(kv.Value, dictB[dateUtc]);
            }
        }

        [Fact]
        public void RegimeDown_DoesNotChange_WhenBaselineExitCloseChanges()
        {
            var tz = CoreNyWindowing.NyTz;

            const int days = 260;

            BuildSyntheticMarket(
                days: days,
                out var solAll6h_A,
                out var btcAll6h,
                out var paxgAll6h,
                out var solWinTrain_A,
                out var btcWinTrain,
                out var paxgWinTrain,
                out var solAll1m,
                out var fng,
                out var dxy);

            CloneMarket(
                solAll6h_A,
                solAll1m,
                out var solAll6h_B,
                out var solAll1m_B);

            var solWinTrain_B = BuildDailyWinTrainFromAll6h(solAll6h_B);

            var entryUtc = FindMidEntryUtc(solWinTrain_A, tz);

            var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), nyTz: CoreNyWindowing.NyTz).Value;
            int exit6hIdx = FindCovering6hCandleIndex(solAll6h_B, exitUtc);
            Assert.True(exit6hIdx >= 0, "Не удалось найти 6h-свечу, покрывающую baseline-exit.");

            Scale6hCandleInPlace(solAll6h_B, exit6hIdx, factor: 10.0);

            var resA = RowBuilder.BuildDailyRows(
                solWinTrain: solWinTrain_A,
                btcWinTrain: btcWinTrain,
                paxgWinTrain: paxgWinTrain,
                solAll6h: solAll6h_A,
                solAll1m: solAll1m,
                fngHistory: fng,
                dxySeries: dxy,
                extraDaily: null,
                nyTz: tz);

            var resB = RowBuilder.BuildDailyRows(
                solWinTrain: solWinTrain_B,
                btcWinTrain: btcWinTrain,
                paxgWinTrain: paxgWinTrain,
                solAll6h: solAll6h_B,
                solAll1m: solAll1m_B,
                fngHistory: fng,
                dxySeries: dxy,
                extraDaily: null,
                nyTz: tz);

            var rowA = resA.CausalRows.SingleOrDefault(r => r.EntryUtc.Value == entryUtc);
            var rowB = resB.CausalRows.SingleOrDefault(r => r.EntryUtc.Value == entryUtc);

            Assert.NotNull(rowA);
            Assert.NotNull(rowB);

            Assert.Equal(rowA!.RegimeDown, rowB!.RegimeDown);
        }

        // ===== helpers =====

        private static void BuildSyntheticMarket(
            int days,
            out List<Candle6h> solAll6h,
            out List<Candle6h> btcAll6h,
            out List<Candle6h> paxgAll6h,
            out List<Candle6h> solWinTrain,
            out List<Candle6h> btcWinTrain,
            out List<Candle6h> paxgWinTrain,
            out List<Candle1m> solAll1m,
            out Dictionary<DateTime, double> fng,
            out Dictionary<DateTime, double> dxy)
        {
            int total6h = days * 4;
            int totalMinutes = days * 24 * 60;

            var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            solAll6h = new List<Candle6h>(total6h);
            btcAll6h = new List<Candle6h>(total6h);
            paxgAll6h = new List<Candle6h>(total6h);
            solAll1m = new List<Candle1m>(totalMinutes);

            for (int i = 0; i < total6h; i++)
            {
                var t = start.AddHours(6 * i);

                double sol = 100.0 + i * 0.1;
                double btc = 500.0 + i * 0.3;
                double paxg = 1500.0 + i * 0.05;

                solAll6h.Add(new Candle6h
                {
                    OpenTimeUtc = t,
                    Open = sol,
                    High = sol * 1.01,
                    Low = sol * 0.99,
                    Close = sol * 1.005
                });

                btcAll6h.Add(new Candle6h
                {
                    OpenTimeUtc = t,
                    Open = btc,
                    High = btc * 1.01,
                    Low = btc * 0.99,
                    Close = btc * 1.005
                });

                paxgAll6h.Add(new Candle6h
                {
                    OpenTimeUtc = t,
                    Open = paxg,
                    High = paxg * 1.01,
                    Low = paxg * 0.99,
                    Close = paxg * 1.005
                });
            }

            for (int i = 0; i < totalMinutes; i++)
            {
                var t = start.AddMinutes(i);

                double p = 100.0 + i * 0.0002;

                solAll1m.Add(new Candle1m
                {
                    OpenTimeUtc = t,
                    Open = p,
                    High = p * 1.0005,
                    Low = p * 0.9995,
                    Close = p
                });
            }

            solWinTrain = BuildDailyWinTrainFromAll6h(solAll6h);
            btcWinTrain = BuildDailyWinTrainFromAll6h(btcAll6h);
            paxgWinTrain = BuildDailyWinTrainFromAll6h(paxgAll6h);

            fng = new Dictionary<DateTime, double>();
            dxy = new Dictionary<DateTime, double>();

            var first = start.ToCausalDateUtc().AddDays(-260);
            var last = start.ToCausalDateUtc().AddDays(days + 260);

            for (var d = first; d <= last; d = d.AddDays(1))
            {
                var key = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
                fng[key] = 50.0;
                dxy[key] = 100.0 + (d.Day % 7) * 0.1;
            }
        }

        private static List<Candle6h> BuildDailyWinTrainFromAll6h(List<Candle6h> all6h)
        {
            // Полный 6h-ряд: RowBuilder сам отфильтрует NY-morning.
            return all6h
                .Select(c => new Candle6h
                {
                    OpenTimeUtc = c.OpenTimeUtc,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close
                })
                .ToList();
        }

        private static DateTime FindMidEntryUtc(List<Candle6h> solWinTrain, TimeZoneInfo tz)
        {
            int start = Math.Min(220, Math.Max(0, solWinTrain.Count / 2));
            int count = Math.Min(80, Math.Max(0, solWinTrain.Count - start - 10));

            for (int i = start; i < start + count; i++)
            {
                var utc = solWinTrain[i].OpenTimeUtc;
                var ny = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                if (!CoreNyWindowing.IsNyMorning(new EntryUtc(utc), tz))
                    continue;

                return utc;
            }

            throw new InvalidOperationException("Не удалось подобрать weekday entryUtc в середине синтетической истории.");
        }

        private static void CloneMarket(
            List<Candle6h> solAll6h_A,
            List<Candle1m> solAll1m_A,
            out List<Candle6h> solAll6h_B,
            out List<Candle1m> solAll1m_B)
        {
            solAll6h_B = solAll6h_A
                .Select(c => new Candle6h
                {
                    OpenTimeUtc = c.OpenTimeUtc,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close
                })
                .ToList();

            solAll1m_B = solAll1m_A
                .Select(c => new Candle1m
                {
                    OpenTimeUtc = c.OpenTimeUtc,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close
                })
                .ToList();
        }

        private static void MutateAllFutureAfterEntry(
            List<Candle6h> solAll6h,
            List<Candle6h> solWinTrain,
            List<Candle1m> solAll1m,
            DateTime entryUtc,
            double sol6hFactor,
            double sol1mFactor)
        {
            for (int i = 0; i < solAll6h.Count; i++)
            {
                var c = solAll6h[i];
                if (c.OpenTimeUtc <= entryUtc) continue;

                c.Open *= sol6hFactor;
                c.High *= sol6hFactor;
                c.Low *= sol6hFactor;
                c.Close *= sol6hFactor;

                solAll6h[i] = c;
            }

            for (int i = 0; i < solWinTrain.Count; i++)
            {
                var c = solWinTrain[i];
                if (c.OpenTimeUtc <= entryUtc) continue;

                c.Open *= sol6hFactor;
                c.High *= sol6hFactor;
                c.Low *= sol6hFactor;
                c.Close *= sol6hFactor;

                solWinTrain[i] = c;
            }

            for (int i = 0; i < solAll1m.Count; i++)
            {
                var m = solAll1m[i];
                if (m.OpenTimeUtc <= entryUtc) continue;

                m.Open *= sol1mFactor;
                m.High *= sol1mFactor;
                m.Low *= sol1mFactor;
                m.Close *= sol1mFactor;

                solAll1m[i] = m;
            }
        }

        private static int FindCovering6hCandleIndex(List<Candle6h> solAll6h, DateTime utc)
        {
            for (int i = 0; i < solAll6h.Count; i++)
            {
                var start = solAll6h[i].OpenTimeUtc;
                var end = (i + 1 < solAll6h.Count) ? solAll6h[i + 1].OpenTimeUtc : start.AddHours(6);
                if (utc >= start && utc < end)
                    return i;
            }

            return -1;
        }

        private static void Scale6hCandleInPlace(List<Candle6h> xs, int idx, double factor)
        {
            var c = xs[idx];

            c.Open *= factor;
            c.High *= factor;
            c.Low *= factor;
            c.Close *= factor;

            xs[idx] = c;
        }
    }
}
