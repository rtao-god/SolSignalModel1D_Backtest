using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
{
    /// <summary>
    /// Жёсткий тест на утечки в RowBuilder:
    /// фичи дневной строки за день D НЕ должны зависеть от свечей и минуток
    /// после baseline-exit (t_exit).
    /// </summary>
    public sealed class RowBuilderFutureTailLeakageTests
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        [Fact]
        public void Features_DoNotChange_WhenFutureTailAfterExitIsMutated()
        {
            var tz = NyTz;

            const int total6h = 400;
            var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var solAll6h_A = new List<Candle6h>();
            var solAll6h_B = new List<Candle6h>();
            var btcAll6h = new List<Candle6h>();
            var paxgAll6h = new List<Candle6h>();

            for (int i = 0; i < total6h; i++)
            {
                var t = start.AddHours(6 * i);
                double solPrice = 100.0 + i;
                double btcPrice = 50.0 + i * 0.5;
                double goldPrice = 1500.0 + i * 0.2;

                solAll6h_A.Add(new Candle6h { OpenTimeUtc = t, Open = solPrice, Close = solPrice, High = solPrice + 1.0, Low = solPrice - 1.0 });
                solAll6h_B.Add(new Candle6h { OpenTimeUtc = t, Open = solPrice, Close = solPrice, High = solPrice + 1.0, Low = solPrice - 1.0 });

                btcAll6h.Add(new Candle6h { OpenTimeUtc = t, Open = btcPrice, Close = btcPrice, High = btcPrice + 1.0, Low = btcPrice - 1.0 });
                paxgAll6h.Add(new Candle6h { OpenTimeUtc = t, Open = goldPrice, Close = goldPrice, High = goldPrice + 1.0, Low = goldPrice - 1.0 });
            }

            var solAll1m_A = new List<Candle1m>();
            var solAll1m_B = new List<Candle1m>();
            int totalMinutes = total6h * 6 * 60;

            for (int i = 0; i < totalMinutes; i++)
            {
                var t = start.AddMinutes(i);
                double price = 100.0 + i * 0.0001;

                solAll1m_A.Add(new Candle1m { OpenTimeUtc = t, Open = price, Close = price, High = price + 0.0005, Low = price - 0.0005 });
                solAll1m_B.Add(new Candle1m { OpenTimeUtc = t, Open = price, Close = price, High = price + 0.0005, Low = price - 0.0005 });
            }

            var fng = new Dictionary<DateTime, double>();
            var dxy = new Dictionary<DateTime, double>();
            Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

            var startDay = start.ToCausalDateUtc();
            var firstDate = startDay.AddDays(-60);
            var lastDate = startDay.AddDays(400);

            for (var d = firstDate; d <= lastDate; d = d.AddDays(1))
            {
                var key = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
                fng[key] = 50;
                dxy[key] = 100.0;
            }

            int entryIdx = Enumerable.Range(200, 50)
                .First(i =>
                {
                    var utc = solAll6h_A[i].OpenTimeUtc;
                    var ny = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                    return ny.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                           && CoreNyWindowing.IsNyMorning(new EntryUtc(utc), tz);
                });

            var entryUtc = solAll6h_A[entryIdx].OpenTimeUtc;
            var entryDate = entryUtc.ToCausalDateUtc();

            var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), tz).Value;

            for (int i = 0; i < solAll6h_B.Count; i++)
            {
                if (solAll6h_B[i].OpenTimeUtc >= exitUtc)
                {
                    solAll6h_B[i].Close *= 10.0;
                    solAll6h_B[i].High = solAll6h_B[i].Close + 5.0;
                    solAll6h_B[i].Low = solAll6h_B[i].Close - 5.0;
                }
            }

            for (int i = 0; i < solAll1m_B.Count; i++)
            {
                if (solAll1m_B[i].OpenTimeUtc >= exitUtc)
                {
                    var p = solAll1m_B[i].Close * 10.0;
                    solAll1m_B[i].Open = p;
                    solAll1m_B[i].Close = p;
                    solAll1m_B[i].High = p + 0.01;
                    solAll1m_B[i].Low = p - 0.01;
                }
            }

            var rowsA = RowBuilder.BuildDailyRows(
                solWinTrain: solAll6h_A,
                btcWinTrain: btcAll6h,
                paxgWinTrain: paxgAll6h,
                solAll6h: solAll6h_A,
                solAll1m: solAll1m_A,
                fngHistory: fng,
                dxySeries: dxy,
                extraDaily: extraDaily,
                nyTz: tz).LabeledRows;

            var rowsB = RowBuilder.BuildDailyRows(
                solWinTrain: solAll6h_B,
                btcWinTrain: btcAll6h,
                paxgWinTrain: paxgAll6h,
                solAll6h: solAll6h_B,
                solAll1m: solAll1m_B,
                fngHistory: fng,
                dxySeries: dxy,
                extraDaily: extraDaily,
                nyTz: tz).LabeledRows;

            var rowA = rowsA.SingleOrDefault(r => r.Causal.EntryUtc.Value.ToCausalDateUtc() == entryDate);
            var rowB = rowsB.SingleOrDefault(r => r.Causal.EntryUtc.Value.ToCausalDateUtc() == entryDate);

            Assert.NotNull(rowA);
            Assert.NotNull(rowB);

            Assert.Equal(rowA!.TrueLabel, rowB!.TrueLabel);

            var fa = rowA.Causal.FeaturesVector.Span;
            var fb = rowB.Causal.FeaturesVector.Span;

            Assert.Equal(fa.Length, fb.Length);
            for (int i = 0; i < fa.Length; i++)
                Assert.Equal(fa[i], fb[i], 10);
        }
    }
}
