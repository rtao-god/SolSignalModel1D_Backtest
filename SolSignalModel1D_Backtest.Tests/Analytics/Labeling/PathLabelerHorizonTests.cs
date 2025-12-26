using SolSignalModel1D_Backtest.Core.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Analytics.Labeling
{
    public sealed class PathLabelerHorizonTests
    {
        [Fact]
        public void Label_DoesNotChange_WhenMinutesAfterExitAreMutated()
        {
            var nyTz = TimeZones.NewYork;

            var entryUtcDt = new DateTime(2020, 2, 24, 15, 0, 0, DateTimeKind.Utc);
            var exitUtc = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtcDt), nyTz);

            double entryPrice = 100.0;
            double minMove = 0.02;

            var minutes = new List<Candle1m>();

            var start = entryUtcDt.AddHours(-1);
            var end = exitUtc.Value.AddHours(12);
            int totalMinutes = (int)(end - start).TotalMinutes;

            for (int i = 0; i <= totalMinutes; i++)
            {
                var t = start.AddMinutes(i);
                double price = entryPrice * (1.0 + 0.0001 * i);

                minutes.Add(new Candle1m
                {
                    OpenTimeUtc = t,
                    Close = price,
                    High = price + 0.0005,
                    Low = price - 0.0005
                });
            }

            var windowA = Baseline1mWindow.Create(minutes, entryUtcDt, exitUtc.Value);

            int labelA = PathLabeler.AssignLabel(
                window: windowA,
                entryPrice: entryPrice,
                minMove: minMove,
                firstPassDir: out int dirA,
                firstPassTimeUtc: out DateTime? timeA,
                reachedUpPct: out double upA,
                reachedDownPct: out double downA);

            var minutesB = minutes
                .Select(m => new Candle1m
                {
                    OpenTimeUtc = m.OpenTimeUtc,
                    Close = m.Close,
                    High = m.High,
                    Low = m.Low
                })
                .ToList();

            foreach (var m in minutesB)
            {
                if (m.OpenTimeUtc >= exitUtc.Value)
                {
                    m.Close *= 10.0;
                    m.High = m.Close + 0.0005;
                    m.Low = m.Close - 0.0005;
                }
            }

            var windowB = Baseline1mWindow.Create(minutesB, entryUtcDt, exitUtc.Value);

            int labelB = PathLabeler.AssignLabel(
                window: windowB,
                entryPrice: entryPrice,
                minMove: minMove,
                firstPassDir: out int dirB,
                firstPassTimeUtc: out DateTime? timeB,
                reachedUpPct: out double upB,
                reachedDownPct: out double downB);

            Assert.Equal(labelA, labelB);
            Assert.Equal(dirA, dirB);
            Assert.Equal(timeA, timeB);
            Assert.Equal(upA, upB, 10);
            Assert.Equal(downA, downB, 10);
        }
    }
}
