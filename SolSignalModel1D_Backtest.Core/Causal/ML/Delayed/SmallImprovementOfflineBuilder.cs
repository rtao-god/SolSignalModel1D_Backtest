using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Delayed
{
    public static class SmallImprovementOfflineBuilder
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
        private static readonly double[] ShallowFactors = new[] { 0.12, 0.18, 0.24 };
        private const double ShallowMaxDelayHours = 2.0;

        /// <summary>
        /// Реальный момент входа (UTC timestamp), типизированный как EntryUtc.
        /// </summary>
        private static EntryUtc EntryUtc(BacktestRecord r) => CausalTimeKey.EntryUtc(r);

        public static List<SmallImprovementSample> Build(
            List<BacktestRecord> rows,
            IReadOnlyList<Candle1h> sol1h,
            Dictionary<DateTime, Candle6h> sol6hDict)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (sol1h == null) throw new ArgumentNullException(nameof(sol1h));
            if (sol6hDict == null) throw new ArgumentNullException(nameof(sol6hDict));

            var res = new List<SmallImprovementSample>(rows.Count * 4);

            if (rows.Count == 0)
                return res;

            if (sol1h.Count == 0)
                throw new ArgumentException("sol1h must be non-empty.", nameof(sol1h));

            if (sol6hDict.Count == 0)
                throw new ArgumentException("sol6hDict must be non-empty.", nameof(sol6hDict));

            foreach (var r in rows)
            {
                var entryTyped = EntryUtc(r);
                var entryUtc = entryTyped.Value;

                if (!sol6hDict.TryGetValue(entryUtc, out var day6))
                {
                    throw new InvalidOperationException(
                        $"[small-impr-offline] sol6hDict has no key for entryUtc={entryUtc:O}. " +
                        "Это рассинхрон пайплайна данных: ключ должен совпадать с реальным EntryUtc.");
                }

                double entry = day6.Close;

                double dayMinMove = r.MinMove;
                if (dayMinMove <= 0 || double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove))
                    throw new InvalidOperationException(
                        $"[small-impr-offline] invalid MinMove for {entryUtc:O}: {dayMinMove}. " +
                        "MinMove должен быть > 0; исправлять нужно генератор MinMove, а не подставлять дефолты.");

                DateTime endUtc;
                try
                {
                    endUtc = NyWindowing.ComputeBaselineExitUtc(entryTyped, NyTz).Value;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to compute baseline exit for entryUtc={entryUtc:O}, tz={NyTz.Id}.",
                        ex);
                }

                var dayHours = sol1h
                    .Where(h => h.OpenTimeUtc >= entryUtc && h.OpenTimeUtc < endUtc)
                    .OrderBy(h => h.OpenTimeUtc)
                    .ToList();

                if (dayHours.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"[small-impr-offline] No 1h candles in window. entryUtc={entryUtc:O}, endUtc={endUtc:O}. " +
                        "Это означает дырку в sol1h или нарушение контракта baseline-окна.");
                }

                BuildForDir(res, r, entryUtc, dayHours, sol1h, entry, dayMinMove, goLong: true, NyTz);
                BuildForDir(res, r, entryUtc, dayHours, sol1h, entry, dayMinMove, goLong: false, NyTz);
            }

            return res;
        }

        private static void BuildForDir(
            List<SmallImprovementSample> sink,
            BacktestRecord r,
            DateTime entryUtc,
            List<Candle1h> dayHours,
            IReadOnlyList<Candle1h> allHours,
            double entryPrice,
            double dayMinMove,
            bool goLong,
            TimeZoneInfo nyTz)
        {
            bool goShort = !goLong;
            bool strong = true;

            var baseOutcome = HourlyTradeEvaluator.EvaluateOne(
                dayHours, entryUtc, goLong, goShort, entryPrice, dayMinMove, strong, nyTz);

            if (baseOutcome.Result != HourlyTradeResult.SlFirst)
                return;

            foreach (var f in ShallowFactors)
            {
                var delayed = DelayedEntryEvaluator.Evaluate(
                    dayHours, entryUtc, goLong, goShort, entryPrice, dayMinMove, strong, f, ShallowMaxDelayHours);

                bool label = false;

                if (delayed.Executed)
                {
                    int baseRank = RankHourly(baseOutcome.Result);
                    int delayedRank = RankDelayed(delayed.Result);

                    if (delayedRank >= baseRank)
                        label = true;
                    else if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
                             delayed.Result == DelayedIntradayResult.SlFirst &&
                             delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
                             delayed.SlPct < baseOutcome.SlPct)
                        label = true;
                }

                var feats = TargetLevelFeatureBuilder.Build(
                    entryUtc, goLong, strong, dayMinMove, entryPrice, allHours);

                sink.Add(new SmallImprovementSample
                {
                    Label = label,
                    Features = feats,
                    EntryUtc = entryUtc
                });
            }
        }

        private static int RankHourly(HourlyTradeResult res) => res switch
        {
            HourlyTradeResult.TpFirst => 3,
            HourlyTradeResult.None => 2,
            HourlyTradeResult.Ambiguous => 2,
            HourlyTradeResult.SlFirst => 0,
            _ => 0
        };

        private static int RankDelayed(DelayedIntradayResult res) => res switch
        {
            DelayedIntradayResult.TpFirst => 3,
            DelayedIntradayResult.None => 2,
            DelayedIntradayResult.Ambiguous => 2,
            DelayedIntradayResult.SlFirst => 0,
            _ => 0
        };
    }
}
