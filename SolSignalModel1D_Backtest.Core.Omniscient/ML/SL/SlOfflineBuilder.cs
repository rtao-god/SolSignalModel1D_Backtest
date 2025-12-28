using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Omniscient.Trading.Evaluator;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;
using Infra = SolSignalModel1D_Backtest.Core.Causal.Infra;

namespace SolSignalModel1D_Backtest.Core.Omniscient.ML.SL
{
    public static class SlOfflineBuilder
    {
        public static List<SlHitSample> Build(
            IReadOnlyList<BacktestRecord> rows,
            IReadOnlyList<Candle1h>? sol1h,
            IReadOnlyList<Candle1m>? sol1m,
            IReadOnlyDictionary<DateTime, Candle6h> sol6hDict,
            double tpPct = 0.03,
            double slPct = 0.05,
            Func<BacktestRecord, bool>? strongSelector = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (sol6hDict == null) throw new ArgumentNullException(nameof(sol6hDict));

            if (sol1m == null || sol1m.Count == 0)
                throw new InvalidOperationException("[sl-offline] sol1m is null/empty: cannot build path-based SL labels.");
            if (sol1h == null || sol1h.Count == 0)
                throw new InvalidOperationException("[sl-offline] sol1h is null/empty: cannot build SL features.");

            var sol1mLocal = sol1m;
            var sol1hLocal = sol1h;

            SeriesGuards.EnsureStrictlyAscendingUtc(rows, r => r.Causal.EntryUtc.Value, "sl-offline.rows");
            SeriesGuards.EnsureStrictlyAscendingUtc(sol1mLocal, c => c.OpenTimeUtc, "sl-offline.sol1m");
            SeriesGuards.EnsureStrictlyAscendingUtc(sol1hLocal, c => c.OpenTimeUtc, "sl-offline.sol1h");

            if (tpPct < 0) throw new ArgumentOutOfRangeException(nameof(tpPct), tpPct, "tpPct must be >= 0.");
            if (slPct < 0) throw new ArgumentOutOfRangeException(nameof(slPct), slPct, "slPct must be >= 0.");
            if (tpPct <= 0 && slPct <= 0)
                throw new ArgumentOutOfRangeException(
                    $"[sl-offline] Invalid config: tpPct<=0 and slPct<=0 (tp={tpPct}, sl={slPct}).");

            var nyTz = NyWindowing.NyTz;

            var result = new List<SlHitSample>(rows.Count * 2);
            bool hasAnyMorning = false;

            foreach (var r in rows)
            {
                var entryUtc = r.Causal.EntryUtc;

                if (!NyWindowing.TryCreateNyTradingEntryUtc(entryUtc, nyTz, out var nyEntryUtc))
                    continue;

                hasAnyMorning = true;

                DateTime entryUtcMoment = nyEntryUtc.Value;

                if (!sol6hDict.TryGetValue(entryUtcMoment, out var c6))
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] 6h candle not found for morning entry {entryUtcMoment:O}. " +
                        "Проверь согласование OpenTimeUtc 6h и EntryUtc.");
                }

                if (c6.OpenTimeUtc != entryUtcMoment)
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] 6h OpenTimeUtc mismatch for entryUtc. entryUtc={entryUtcMoment:O}, c6.OpenTimeUtc={c6.OpenTimeUtc:O}.");
                }

                double entryPrice = c6.Open;
                if (entryPrice <= 0)
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] Non-positive entry price from 6h open for {entryUtcMoment:O}: entry={entryPrice}.");
                }

                double dayMinMove = r.MinMove;
                if (dayMinMove <= 0.0 || double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove))
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] Invalid dayMinMove (MinMove) for {entryUtcMoment:O}: {dayMinMove}.");
                }

                bool strongSignal = strongSelector?.Invoke(r) ?? true;

                using var _ = Infra.Causality.CausalityGuard.Begin("SlOfflineBuilder.Build(morning)", entryUtcMoment);

                DateTime exitUtcExclusive;
                try
                {
                    exitUtcExclusive = NyWindowing.ComputeBaselineExitUtc(nyEntryUtc, nyTz).Value;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] Failed to compute baseline-exit for entry {entryUtcMoment:O}.",
                        ex);
                }

                if (exitUtcExclusive <= entryUtcMoment)
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] Invalid baseline window: exitUtcExclusive <= entryUtc for entry {entryUtcMoment:O}, exit={exitUtcExclusive:O}.");
                }

                int startIdx = LowerBoundOpenTimeUtc(sol1mLocal, entryUtcMoment);
                int endIdxExclusive = LowerBoundOpenTimeUtc(sol1mLocal, exitUtcExclusive);

                if (endIdxExclusive <= startIdx)
                {
                    throw new InvalidOperationException(
                        $"[sl-offline] No 1m candles in baseline window for entry {entryUtcMoment:O}, exitEx={exitUtcExclusive:O}. " +
                        $"Computed range=[{startIdx}; {endIdxExclusive}).");
                }

                // LONG
                {
                    var labelRes = EvalPath1m(
                        all1m: sol1mLocal,
                        startIdx: startIdx,
                        endIdxExclusive: endIdxExclusive,
                        goLong: true,
                        entry: entryPrice,
                        tpPct: tpPct,
                        slPct: slPct);

                    if (labelRes == HourlyTradeResult.SlFirst || labelRes == HourlyTradeResult.TpFirst)
                    {
                        var feats = SlFeatureBuilder.Build(
                            entryUtc: entryUtcMoment,
                            goLong: true,
                            strongSignal: strongSignal,
                            dayMinMove: dayMinMove,
                            entryPrice: entryPrice,
                            candles1h: sol1hLocal
                        );

                        if (feats.Length != SlSchema.FeatureCount)
                        {
                            throw new InvalidOperationException(
                                $"[sl-offline] SlFeatureBuilder returned invalid length: got={feats.Length}, expected={SlSchema.FeatureCount}.");
                        }

                        result.Add(new SlHitSample
                        {
                            Label = labelRes == HourlyTradeResult.SlFirst,
                            Features = feats,
                            EntryUtc = entryUtcMoment
                        });
                    }
                }

                // SHORT
                {
                    var labelRes = EvalPath1m(
                        all1m: sol1mLocal,
                        startIdx: startIdx,
                        endIdxExclusive: endIdxExclusive,
                        goLong: false,
                        entry: entryPrice,
                        tpPct: tpPct,
                        slPct: slPct);

                    if (labelRes == HourlyTradeResult.SlFirst || labelRes == HourlyTradeResult.TpFirst)
                    {
                        var feats = SlFeatureBuilder.Build(
                            entryUtc: entryUtcMoment,
                            goLong: false,
                            strongSignal: strongSignal,
                            dayMinMove: dayMinMove,
                            entryPrice: entryPrice,
                            candles1h: sol1hLocal
                        );

                        if (feats.Length != SlSchema.FeatureCount)
                        {
                            throw new InvalidOperationException(
                                $"[sl-offline] SlFeatureBuilder returned invalid length: got={feats.Length}, expected={SlSchema.FeatureCount}.");
                        }

                        result.Add(new SlHitSample
                        {
                            Label = labelRes == HourlyTradeResult.SlFirst,
                            Features = feats,
                            EntryUtc = entryUtcMoment
                        });
                    }
                }
            }

            if (!hasAnyMorning)
                return result;

            Console.WriteLine(
                $"[sl-offline] built {result.Count} SL-samples (1m path labels, 1h features, tp={tpPct:0.###}, sl={slPct:0.###})");

            return result;
        }

        private static HourlyTradeResult EvalPath1m(
            IReadOnlyList<Candle1m> all1m,
            int startIdx,
            int endIdxExclusive,
            bool goLong,
            double entry,
            double tpPct,
            double slPct)
        {
            if (all1m == null) throw new ArgumentNullException(nameof(all1m));
            if (startIdx < 0 || endIdxExclusive > all1m.Count || endIdxExclusive <= startIdx)
                throw new ArgumentOutOfRangeException(
                    $"Invalid 1m range: [{startIdx}; {endIdxExclusive}) for all1m.Count={all1m.Count}.");

            if (entry <= 0)
                throw new InvalidOperationException($"[sl-offline] entry must be > 0, got {entry}.");

            if (tpPct < 0 || slPct < 0)
                throw new ArgumentOutOfRangeException($"[sl-offline] tpPct/slPct must be >=0. got tp={tpPct}, sl={slPct}.");

            if (tpPct <= 0 && slPct <= 0)
                throw new InvalidOperationException($"[sl-offline] tpPct<=0 and slPct<=0 is invalid for path-eval.");

            if (goLong)
            {
                double tp = tpPct > 0 ? entry * (1.0 + tpPct) : double.NaN;
                double sl = slPct > 0 ? entry * (1.0 - slPct) : double.NaN;

                for (int i = startIdx; i < endIdxExclusive; i++)
                {
                    var m = all1m[i];

                    bool hitTp = tpPct > 0 && m.High >= tp;
                    bool hitSl = slPct > 0 && m.Low <= sl;

                    if (hitTp || hitSl)
                    {
                        if (hitTp && hitSl)
                            return HourlyTradeResult.SlFirst;

                        return hitSl ? HourlyTradeResult.SlFirst : HourlyTradeResult.TpFirst;
                    }
                }
            }
            else
            {
                double tp = tpPct > 0 ? entry * (1.0 - tpPct) : double.NaN;
                double sl = slPct > 0 ? entry * (1.0 + slPct) : double.NaN;

                for (int i = startIdx; i < endIdxExclusive; i++)
                {
                    var m = all1m[i];

                    bool hitTp = tpPct > 0 && m.Low <= tp;
                    bool hitSl = slPct > 0 && m.High >= sl;

                    if (hitTp || hitSl)
                    {
                        if (hitTp && hitSl)
                            return HourlyTradeResult.SlFirst;

                        return hitSl ? HourlyTradeResult.SlFirst : HourlyTradeResult.TpFirst;
                    }
                }
            }

            return HourlyTradeResult.None;
        }

        private static int LowerBoundOpenTimeUtc(IReadOnlyList<Candle1m> all1m, DateTime t)
        {
            int lo = 0;
            int hi = all1m.Count;

            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (all1m[mid].OpenTimeUtc < t)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }
    }
}

