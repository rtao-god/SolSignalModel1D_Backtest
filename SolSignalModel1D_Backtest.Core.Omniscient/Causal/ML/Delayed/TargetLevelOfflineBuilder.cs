using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Omniscient.Trading.Evaluator;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Causal.ML.Delayed
{
    public static class TargetLevelOfflineBuilder
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
        private const double DeepDelayFactor = 0.35;
        private const double DeepMaxDelayHours = 4.0;
        private const double ShallowDelayFactor = 0.15;
        private const double ShallowMaxDelayHours = 2.0;

        public static List<TargetLevelSample> Build(
            List<BacktestRecord> rows,
            IReadOnlyList<Candle1h> sol1h,
            IReadOnlyDictionary<EntryDayKeyUtc, Candle6h> sol6hByDayKey)
        {
            var result = new List<TargetLevelSample>((rows?.Count ?? 0) * 2);
            if (rows == null || rows.Count == 0) return result;
            if (sol1h == null || sol1h.Count == 0) return result;
            if (sol6hByDayKey == null || sol6hByDayKey.Count == 0) return result;

            foreach (var r in rows)
            {
                var dayKey = r.EntryDayKeyUtc;

                if (!sol6hByDayKey.TryGetValue(dayKey, out var dayCandle))
                    continue;

                double entryPrice = dayCandle.Open;
                double dayMinMove = r.MinMove;

                if (double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove) || dayMinMove <= 0.0)
                {
                    throw new InvalidOperationException(
                        $"[target-level-offline] Invalid dayMinMove for dayKey={dayKey.Value:O}. " +
                        $"dayMinMove={dayMinMove}. Fix RowBuilder/MinMoveEngine; do not default here.");
                }

                var entryUtcTyped = new EntryUtc(r.Causal.EntryUtc.Value);
                DateTime endUtc;

                try
                {
                    endUtc = NyWindowing.ComputeBaselineExitUtc(entryUtcTyped, NyTz).Value;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to compute baseline exit for entryUtc={entryUtcTyped.Value:O}, tz={NyTz.Id}. " +
                        "Fix data/NyWindowing logic instead of relying on fallback.",
                        ex);
                }

                var dayHours = sol1h
                    .Where(h => h.OpenTimeUtc >= entryUtcTyped.Value && h.OpenTimeUtc < endUtc)
                    .OrderBy(h => h.OpenTimeUtc)
                    .ToList();

                if (dayHours.Count == 0)
                    continue;

                BuildForDir(result, r, entryUtcTyped.Value, dayKey, dayHours, sol1h, entryPrice, dayMinMove, goLong: true, NyTz);
                BuildForDir(result, r, entryUtcTyped.Value, dayKey, dayHours, sol1h, entryPrice, dayMinMove, goLong: false, NyTz);
            }

            return result;
        }

        private static void BuildForDir(
            List<TargetLevelSample> sink,
            BacktestRecord r,
            DateTime entryUtc,
            EntryDayKeyUtc dayKey,
            List<Candle1h> dayHours,
            IReadOnlyList<Candle1h> allHours,
            double entryPrice,
            double dayMinMove,
            bool goLong,
            TimeZoneInfo nyTz)
        {
            bool strongSignal = true;

            var baseOutcome = HourlyTradeEvaluator.EvaluateOne(
                dayHours, entryUtc, goLong, !goLong, entryPrice, dayMinMove, strongSignal, nyTz);

            var deepDelayed = DelayedEntryEvaluator.Evaluate(
                dayHours, entryUtc, goLong, !goLong, entryPrice, dayMinMove, strongSignal, DeepDelayFactor, DeepMaxDelayHours);

            var shDelayed = DelayedEntryEvaluator.Evaluate(
                dayHours, entryUtc, goLong, !goLong, entryPrice, dayMinMove, strongSignal, ShallowDelayFactor, ShallowMaxDelayHours);

            int label = 0;
            if (deepDelayed.Executed && IsDeepImprovement(baseOutcome, deepDelayed))
                label = 2;
            else if (shDelayed.Executed && IsShallowNotWorse(baseOutcome, shDelayed))
                label = 1;

            var feats = TargetLevelFeatureBuilder.Build(
                 entryUtc, goLong, strongSignal, dayMinMove, entryPrice, allHours);

            sink.Add(new TargetLevelSample
            {
                Label = label,
                Features = feats,
                EntryUtc = entryUtc
            });
        }

        private static bool IsDeepImprovement(HourlyTradeOutcome baseOutcome, DelayedEntryResult delayed)
        {
            if ((baseOutcome.Result == HourlyTradeResult.SlFirst || baseOutcome.Result == HourlyTradeResult.None) &&
                delayed.Result == DelayedIntradayResult.TpFirst)
                return true;

            if (baseOutcome.Result == HourlyTradeResult.TpFirst &&
                delayed.Result == DelayedIntradayResult.TpFirst)
                return true;

            if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
                delayed.Result == DelayedIntradayResult.SlFirst &&
                delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
                delayed.SlPct < baseOutcome.SlPct)
                return true;

            return false;
        }

        private static bool IsShallowNotWorse(HourlyTradeOutcome baseOutcome, DelayedEntryResult delayed)
        {
            int baseRank = RankHourly(baseOutcome.Result);
            int delayedRank = RankDelayed(delayed.Result);

            if (delayedRank < baseRank) return false;

            if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
                delayed.Result == DelayedIntradayResult.SlFirst &&
                delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
                delayed.SlPct < baseOutcome.SlPct)
                return true;

            if (delayed.Result == DelayedIntradayResult.None &&
                baseOutcome.Result == HourlyTradeResult.SlFirst)
                return true;

            return delayedRank >= baseRank;
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

