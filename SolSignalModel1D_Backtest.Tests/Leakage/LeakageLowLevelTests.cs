using SolSignalModel1D_Backtest.Core.Causal.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Trading.Evaluator;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;

namespace SolSignalModel1D_Backtest.Tests.Leakage
{
    public class LeakageLowLevelTests
    {
        [Fact]
        public void PathLabeler_Ignores_Minutes_After_Baseline_Exit()
        {
            var nyTz = TimeZones.NewYork;
            var nyLocal = new DateTime(2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified);
            var entryUtc = TimeZoneInfo.ConvertTimeToUtc(nyLocal, nyTz);

            var endUtc = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), nyTz).Value;

            double entryPrice = 100.0;
            double minMove = 0.02;

            var minutesBase = BuildMinuteCandles(entryUtc, endUtc.AddHours(6), entryPrice, 0.0005);

            var minutesMutated = minutesBase
                .Select(c => new Candle1m
                {
                    OpenTimeUtc = c.OpenTimeUtc,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                })
                .ToList();

            foreach (var m in minutesMutated.Where(m => m.OpenTimeUtc >= endUtc))
            {
                m.High *= 1.50;
                m.Low *= 0.50;
                m.Close *= 1.20;
            }

            var w1 = Baseline1mWindow.Create(minutesBase, entryUtc, endUtc);
            var w2 = Baseline1mWindow.Create(minutesMutated, entryUtc, endUtc);

            var label1 = PathLabeler.AssignLabel(
                window: w1,
                entryPrice: entryPrice,
                minMove: minMove,
                firstPassDir: out var firstDir1,
                firstPassTimeUtc: out var firstTime1,
                reachedUpPct: out var up1,
                reachedDownPct: out var down1,
                ambiguousHitSameMinute: out var amb1);

            var label2 = PathLabeler.AssignLabel(
                window: w2,
                entryPrice: entryPrice,
                minMove: minMove,
                firstPassDir: out var firstDir2,
                firstPassTimeUtc: out var firstTime2,
                reachedUpPct: out var up2,
                reachedDownPct: out var down2,
                ambiguousHitSameMinute: out var amb2);

            Assert.Equal(label1, label2);
            Assert.Equal(firstDir1, firstDir2);
            Assert.Equal(firstTime1, firstTime2);
            Assert.Equal(up1, up2, 10);
            Assert.Equal(down1, down2, 10);
            Assert.Equal(amb1, amb2);
        }

        [Fact]
        public void Baseline1mWindow_Uses_Exclusive_Exit_And_Full_Minute_Count()
        {
            var nyTz = TimeZones.NewYork;
            var nyLocal = new DateTime(2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified);
            var entryUtc = TimeZoneInfo.ConvertTimeToUtc(nyLocal, nyTz);

            var exitUtcExclusive = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), nyTz).Value;

            var minutes = BuildMinuteCandles(entryUtc, exitUtcExclusive.AddHours(1), 100.0, 0.0001);
            var window = Baseline1mWindow.Create(minutes, entryUtc, exitUtcExclusive);

            int expectedCount = (int)(exitUtcExclusive - entryUtc).TotalMinutes;
            Assert.Equal(expectedCount, window.Count);

            var lastOpen = window[window.Count - 1].OpenTimeUtc;
            Assert.Equal(exitUtcExclusive.AddMinutes(-1), lastOpen);
        }

        [Fact]
        public void MinMoveEngine_Uses_Only_Past_HistoryRows()
        {
            var asOfUtc = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc);

            var baseHistory = new List<MinMoveHistoryRow>();
            var start = asOfUtc.AddDays(-30);
            var rand = new Random(42);

            for (int i = 0; i <= 30; i++)
            {
                var date = start.AddDays(i);
                baseHistory.Add(new MinMoveHistoryRow(
                    DateUtc: date,
                    RealizedPathAmpPct: 0.02 + rand.NextDouble() * 0.03));
            }

            var historyWithFuture = new List<MinMoveHistoryRow>(baseHistory);
            for (int i = 1; i <= 10; i++)
            {
                var futureDate = asOfUtc.AddDays(i);
                historyWithFuture.Add(new MinMoveHistoryRow(
                    DateUtc: futureDate,
                    RealizedPathAmpPct: 0.50));
            }

            var cfg = new MinMoveConfig
            {
                QuantileStart = 0.5,
                QuantileLow = 0.2,
                QuantileHigh = 0.9,
                QuantileWindowDays = 30,
                QuantileRetuneEveryDays = 5,
                AtrWeight = 0.5,
                DynVolWeight = 0.5,
                MinFloorPct = 0.01,
                MinCeilPct = 0.10,
                RegimeDownMul = 1.5
            };

            var state1 = new MinMoveState { EwmaVol = 0.0, QuantileQ = 0.0, LastQuantileTune = DateTime.MinValue };
            var state2 = new MinMoveState { EwmaVol = 0.0, QuantileQ = 0.0, LastQuantileTune = DateTime.MinValue };

            double atrPct = 0.03;
            double dynVol = 0.025;

            var r1 = MinMoveEngine.ComputeAdaptive(asOfUtc, false, atrPct, dynVol, baseHistory, cfg, state1);
            var r2 = MinMoveEngine.ComputeAdaptive(asOfUtc, false, atrPct, dynVol, historyWithFuture, cfg, state2);

            Assert.Equal(r1.MinMove, r2.MinMove, 6);
            Assert.Equal(r1.QuantileUsed, r2.QuantileUsed, 6);
            Assert.Equal(r1.EwmaVol, r2.EwmaVol, 6);
        }

        [Fact]
        public void TargetLevelFeatureBuilder_Ignores_1h_Bars_After_Entry()
        {
            var entryUtc = new DateTime(2025, 4, 10, 12, 0, 0, DateTimeKind.Utc);
            double entryPrice = 100.0;
            double dayMinMove = 0.03;
            bool goLong = true;
            bool strongSignal = true;

            var startUtc = entryUtc.AddHours(-6);
            var candlesAll = BuildHourlyCandles(startUtc, hours: 12, basePrice: entryPrice, stepPerHour: 0.002);

            var candlesBefore = candlesAll
                .Where(c => c.OpenTimeUtc < entryUtc)
                .ToList();

            Assert.Contains(candlesAll, c => c.OpenTimeUtc >= entryUtc);
            Assert.Equal(6, candlesBefore.Count);

            var featsAll = TargetLevelFeatureBuilder.Build(entryUtc, goLong, strongSignal, dayMinMove, entryPrice, candlesAll);
            var featsBefore = TargetLevelFeatureBuilder.Build(entryUtc, goLong, strongSignal, dayMinMove, entryPrice, candlesBefore);

            Assert.Equal(featsAll.Length, featsBefore.Length);
            for (int i = 0; i < featsAll.Length; i++)
                Assert.Equal(featsAll[i], featsBefore[i], 6);
        }

        [Fact]
        public void MinuteDelayedEntryEvaluator_Ignores_Minutes_After_Baseline_Exit()
        {
            var nyTz = TimeZones.NewYork;

            var nyLocal = new DateTime(2025, 5, 20, 8, 0, 0, DateTimeKind.Unspecified);
            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(nyLocal, nyTz);

            var baselineExit = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(dayStartUtc), nyTz).Value;

            double entryPrice = 100.0;
            double dayMinMove = 0.03;
            bool goLong = true;
            bool goShort = false;
            bool strongSignal = true;
            double delayFactor = 0.35;
            double maxDelayHours = 4.0;

            var day1mBase = BuildMinuteCandles(dayStartUtc, baselineExit.AddHours(3), entryPrice, 0.0003);

            var day1mMutated = day1mBase
                .Select(c => new Candle1m
                {
                    OpenTimeUtc = c.OpenTimeUtc,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                })
                .ToList();

            foreach (var m in day1mMutated.Where(m => m.OpenTimeUtc >= baselineExit))
            {
                m.High *= 2.0;
                m.Low *= 0.5;
                m.Close *= 1.5;
            }

            var res1 = MinuteDelayedEntryEvaluator.Evaluate(
                day1m: day1mBase,
                dayStartUtc: dayStartUtc,
                goLong: goLong,
                goShort: goShort,
                entryPrice12: entryPrice,
                dayMinMove: dayMinMove,
                strongSignal: strongSignal,
                delayFactor: delayFactor,
                maxDelayHours: maxDelayHours,
                nyTz: nyTz);

            var res2 = MinuteDelayedEntryEvaluator.Evaluate(
                day1m: day1mMutated,
                dayStartUtc: dayStartUtc,
                goLong: goLong,
                goShort: goShort,
                entryPrice12: entryPrice,
                dayMinMove: dayMinMove,
                strongSignal: strongSignal,
                delayFactor: delayFactor,
                maxDelayHours: maxDelayHours,
                nyTz: nyTz);

            Assert.Equal(res1.Executed, res2.Executed);
            Assert.Equal(res1.Result, res2.Result);
            Assert.Equal(res1.TpPct, res2.TpPct, 6);
            Assert.Equal(res1.SlPct, res2.SlPct, 6);
            Assert.Equal(res1.TargetEntryPrice, res2.TargetEntryPrice, 6);
            Assert.Equal(res1.ExecutedAtUtc, res2.ExecutedAtUtc);
        }

        private static List<Candle1m> BuildMinuteCandles(DateTime startUtc, DateTime endUtc, double startPrice, double stepPerMinute)
        {
            var result = new List<Candle1m>();
            var t = startUtc;
            double price = startPrice;

            while (t < endUtc)
            {
                double high = price * (1.0 + 0.001);
                double low = price * (1.0 - 0.001);

                result.Add(new Candle1m
                {
                    OpenTimeUtc = t,
                    Open = price,
                    High = high,
                    Low = low,
                    Close = price,
                });

                price *= 1.0 + stepPerMinute;
                t = t.AddMinutes(1);
            }

            return result;
        }

        private static List<Candle1h> BuildHourlyCandles(DateTime startUtc, int hours, double basePrice, double stepPerHour)
        {
            var result = new List<Candle1h>();
            var t = startUtc;
            double price = basePrice;

            for (int i = 0; i < hours; i++)
            {
                double open = price;
                double close = price * (1.0 + stepPerHour);
                double high = Math.Max(open, close) * 1.001;
                double low = Math.Min(open, close) * 0.999;

                result.Add(new Candle1h
                {
                    OpenTimeUtc = t,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                });

                price = close;
                t = t.AddHours(1);
            }

            return result;
        }
    }
}
