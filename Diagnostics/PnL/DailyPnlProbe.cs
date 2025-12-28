using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Diagnostics.PnL
{
    public static class DailyPnlProbe
    {
        public static void RunSimpleProbe(
            IReadOnlyList<BacktestRecord> records,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz)
        {
            if (records == null || records.Count == 0)
            {
                Console.WriteLine("[pnl-probe] records is null or empty – nothing to compute.");
                return;
            }

            if (trainUntilExitDayKeyUtc.IsDefault)
            {
                Console.WriteLine("[pnl-probe] trainUntilExitDayKeyUtc is default – nothing to compute.");
                return;
            }

            if (nyTz == null)
            {
                Console.WriteLine("[pnl-probe] nyTz is null – nothing to compute.");
                return;
            }

            var ordered = records
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: r => r.Causal.EntryUtc,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz);

            var train = split.Train;
            var oos = split.Oos;

            Console.WriteLine(
                $"[pnl-probe] trainUntilExitDayKey = {NyTrainSplit.ToIsoDate(trainUntilExitDayKeyUtc)}, " +
                $"totalRecords={ordered.Count}, train={train.Count}, oos={oos.Count}, excluded={split.Excluded.Count}");

            if (train.Count == 0 || oos.Count == 0)
            {
                Console.WriteLine("[pnl-probe] WARNING: one of splits (train/OOS) is empty – results may be uninformative.");
            }

            if (split.Excluded.Count > 0)
            {
                Console.WriteLine("[pnl-probe] WARNING: excluded days exist (baseline-exit undefined). They are ignored.");
            }

            var trainStats = ComputeSimplePnlStats(train);
            var oosStats = ComputeSimplePnlStats(oos);

            PrintStats("[pnl-probe] TRAIN", trainStats);
            PrintStats("[pnl-probe] OOS  ", oosStats);
        }

        private static SimplePnlStats ComputeSimplePnlStats(IReadOnlyList<BacktestRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                return SimplePnlStats.Empty;
            }

            int trades = 0;
            int wins = 0;

            var returns = new List<double>(records.Count);

            double equity = 1.0;
            double peakEquity = 1.0;
            double maxDrawdown = 0.0;

            foreach (var rec in records)
            {
                var c = rec.Causal;
                var f = rec.Forward;

                bool goLong =
                    c.PredLabel == 2 ||
                    (c.PredLabel == 1 && c.PredMicroUp);

                bool goShort =
                    c.PredLabel == 0 ||
                    (c.PredLabel == 1 && c.PredMicroDown);

                if (!goLong && !goShort)
                {
                    continue;
                }

                if (f.Entry <= 0.0 || f.Close24 <= 0.0)
                {
                    var day = c.EntryDayKeyUtc.Value;

                    Console.WriteLine(
                        $"[pnl-probe] skip {day:yyyy-MM-dd}: invalid prices Entry={f.Entry}, Close24={f.Close24}");
                    continue;
                }

                double dayRet = (f.Close24 - f.Entry) / f.Entry;

                if (goShort && !goLong)
                {
                    dayRet = -dayRet;
                }
                else if (goLong && goShort)
                {
                    var day = c.EntryDayKeyUtc.Value;

                    Console.WriteLine(
                        $"[pnl-probe] ambiguous direction on {day:yyyy-MM-dd}, " +
                        $"PredLabel={c.PredLabel}, PredMicroUp={c.PredMicroUp}, PredMicroDown={c.PredMicroDown} – skip.");
                    continue;
                }

                trades++;
                returns.Add(dayRet);

                if (dayRet > 0)
                {
                    wins++;
                }

                equity *= (1.0 + dayRet);

                if (equity > peakEquity)
                {
                    peakEquity = equity;
                }

                double dd = equity / peakEquity - 1.0;
                if (dd < maxDrawdown)
                {
                    maxDrawdown = dd;
                }
            }

            if (trades == 0 || returns.Count == 0)
            {
                return SimplePnlStats.Empty;
            }

            double totalRet = equity - 1.0;
            double winRate = (double)wins / trades;

            double mean = returns.Average();
            double variance = returns
                .Select(r => (r - mean) * (r - mean))
                .DefaultIfEmpty(0.0)
                .Average();

            double std = Math.Sqrt(variance);

            return new SimplePnlStats(
                Trades: trades,
                TotalReturn: totalRet,
                WinRate: winRate,
                MaxDrawdown: maxDrawdown,
                MeanReturn: mean,
                StdReturn: std);
        }

        private static void PrintStats(string prefix, SimplePnlStats stats)
        {
            if (stats.Trades == 0)
            {
                Console.WriteLine($"{prefix}: no trades.");
                return;
            }

            Console.WriteLine(
                $"{prefix}: trades={stats.Trades}, " +
                $"totalPnL={stats.TotalReturn * 100.0:0.00} %, " +
                $"winRate={stats.WinRate * 100.0:0.0} %, " +
                $"maxDD={stats.MaxDrawdown * 100.0:0.0} %, " +
                $"mean={stats.MeanReturn * 100.0:0.00} %, " +
                $"std={stats.StdReturn * 100.0:0.00} %");
        }

        private readonly record struct SimplePnlStats(
            int Trades,
            double TotalReturn,
            double WinRate,
            double MaxDrawdown,
            double MeanReturn,
            double StdReturn)
        {
            public static readonly SimplePnlStats Empty = new(
                Trades: 0,
                TotalReturn: 0.0,
                WinRate: 0.0,
                MaxDrawdown: 0.0,
                MeanReturn: 0.0,
                StdReturn: 0.0);
        }
    }
}

