using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Trading.Evaluator
{
    public sealed class HourlyTpSlReport
    {
        public double TotalPnlPct { get; set; }
        public double TotalPnlMultiplier { get; set; }
        public double MaxDrawdownPct { get; set; }

        public int Trades { get; set; }
        public int TpFirst { get; set; }
        public int SlFirst { get; set; }
        public int Ambiguous { get; set; }
    }

    public enum HourlyTradeResult
    {
        None = 0,
        TpFirst = 1,
        SlFirst = 2,
        Ambiguous = 3
    }

    public sealed class HourlyTradeOutcome
    {
        public HourlyTradeResult Result { get; set; }
        public double TpPct { get; set; }
        public double SlPct { get; set; }
    }

    public static class HourlyTradeEvaluator
    {
        private const double MinDayTradeable = 0.018;

        private const double StrongTpMul = 1.25;
        private const double StrongSlMul = 0.55;
        private const double WeakTpMul = 1.10;
        private const double WeakSlMul = 0.50;

        private const double StrongTpFloor = 0.022;
        private const double StrongSlFloor = 0.009;
        private const double WeakTpFloor = 0.017;
        private const double WeakSlFloor = 0.008;

        public static HourlyTradeOutcome EvaluateOne(
            IReadOnlyList<Candle1h> candles1h,
            DateTime entryUtc,
            bool goLong,
            bool goShort,
            double entryPrice,
            double dayMinMove,
            bool strongSignal,
            TimeZoneInfo nyTz)
        {
            if (!TryEvaluateOne(
                    candles1h: candles1h,
                    entryUtc: entryUtc,
                    goLong: goLong,
                    goShort: goShort,
                    entryPrice: entryPrice,
                    dayMinMove: dayMinMove,
                    strongSignal: strongSignal,
                    nyTz: nyTz,
                    out var outcome))
            {
                return outcome;
            }

            return outcome;
        }

        public static bool TryEvaluateOne(
            IReadOnlyList<Candle1h> candles1h,
            DateTime entryUtc,
            bool goLong,
            bool goShort,
            double entryPrice,
            double dayMinMove,
            bool strongSignal,
            TimeZoneInfo nyTz,
            out HourlyTradeOutcome outcome)
        {
            outcome = new HourlyTradeOutcome
            {
                Result = HourlyTradeResult.None,
                TpPct = 0.0,
                SlPct = 0.0
            };

            if (candles1h == null)
                throw new ArgumentNullException(nameof(candles1h), "[hourly] candles1h is required.");

            if (candles1h.Count == 0)
                throw new InvalidOperationException("[hourly] candles1h is empty: cannot evaluate.");

            if (nyTz == null)
                throw new ArgumentNullException(nameof(nyTz));

            if (entryUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[hourly] entryUtc must be UTC, got Kind={entryUtc.Kind}, t={entryUtc:O}.");

            if (goLong == goShort)
            {
                if (!goLong)
                    return false;

                throw new InvalidOperationException("[hourly] Invalid direction: expected goLong XOR goShort.");
            }

            if (entryPrice <= 0.0 || double.IsNaN(entryPrice) || double.IsInfinity(entryPrice))
                throw new ArgumentOutOfRangeException(nameof(entryPrice), entryPrice, "[hourly] entryPrice must be finite and > 0.");

            if (double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove) || dayMinMove <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dayMinMove),
                    dayMinMove,
                    "[hourly] dayMinMove must be finite and > 0. " +
                    "Non-positive/NaN/Inf dayMinMove indicates an upstream bug (NyWindowing/features/labeling).");
            }

            if (dayMinMove < MinDayTradeable)
                return false;

            EnsureStrictlyAscendingUtc(candles1h, c => c.OpenTimeUtc, "hourly.candles1h");

            DateTime endUtc;
            try
            {
                endUtc = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), nyTz).Value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[hourly] Failed to compute baseline-exit for entry {entryUtc:O}. " +
                    "Weekend entries must not reach HourlyTradeEvaluator by contract.",
                    ex);
            }

            if (endUtc <= entryUtc)
                throw new InvalidOperationException($"[hourly] Invalid baseline window: endUtc<=entryUtc for {entryUtc:O}, end={endUtc:O}.");

            int startIdx = LowerBoundOpenTimeUtc(candles1h, entryUtc);
            int endIdxExclusive = LowerBoundOpenTimeUtc(candles1h, endUtc);

            if (endIdxExclusive <= startIdx)
                return false;

            ComputeTpSlPct(dayMinMove, strongSignal, out double tpPct, out double slPct);
            outcome.TpPct = tpPct;
            outcome.SlPct = slPct;

            double tpPrice, slPrice;
            if (goLong)
            {
                tpPrice = entryPrice * (1.0 + tpPct);
                slPrice = entryPrice * (1.0 - slPct);
            }
            else
            {
                tpPrice = entryPrice * (1.0 - tpPct);
                slPrice = entryPrice * (1.0 + slPct);
            }

            for (int i = startIdx; i < endIdxExclusive; i++)
            {
                var bar = candles1h[i];

                if (goLong)
                {
                    bool tpInBar = bar.High >= tpPrice;
                    bool slInBar = bar.Low <= slPrice;

                    if (tpInBar && slInBar) { outcome.Result = HourlyTradeResult.Ambiguous; return true; }
                    if (tpInBar) { outcome.Result = HourlyTradeResult.TpFirst; return true; }
                    if (slInBar) { outcome.Result = HourlyTradeResult.SlFirst; return true; }
                }
                else
                {
                    bool tpInBar = bar.Low <= tpPrice;
                    bool slInBar = bar.High >= slPrice;

                    if (tpInBar && slInBar) { outcome.Result = HourlyTradeResult.Ambiguous; return true; }
                    if (tpInBar) { outcome.Result = HourlyTradeResult.TpFirst; return true; }
                    if (slInBar) { outcome.Result = HourlyTradeResult.SlFirst; return true; }
                }
            }

            outcome.Result = HourlyTradeResult.None;
            return true;
        }

        public static HourlyTpSlReport Evaluate(
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1h> candles1h,
            TimeZoneInfo nyTz)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (candles1h == null) throw new ArgumentNullException(nameof(candles1h));
            if (candles1h.Count == 0) throw new InvalidOperationException("[hourly] candles1h is empty: cannot evaluate report.");
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            EnsureStrictlyAscendingUtc(candles1h, c => c.OpenTimeUtc, "hourly.candles1h");
            EnsureStrictlyAscendingUtc(records, r => r.Causal.EntryUtc.Value, "hourly.records");

            var report = new HourlyTpSlReport();

            double equity = 1.0;
            double peak = 1.0;
            double maxDd = 0.0;

            for (int ri = 0; ri < records.Count; ri++)
            {
                var rec = records[ri];

                bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
                bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);

                if (!goLong && !goShort)
                    continue;

                if (goLong && goShort)
                {
                    throw new InvalidOperationException(
                        $"[hourly] Conflicting direction for entry={rec.Causal.EntryUtc.Value:O} dayKey={rec.EntryDayKeyUtc.Value:O}: goLong=true and goShort=true. " +
                        "Fix the upstream label aggregation (PredLabel/Micro flags).");
                }

                double dayMinMove = rec.MinMove;
                if (double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove) || dayMinMove <= 0.0)
                {
                    throw new InvalidOperationException(
                        $"[hourly] Invalid rec.MinMove={dayMinMove} for entry={rec.Causal.EntryUtc.Value:O}. " +
                        "MinMove must be finite and > 0. Fix the upstream MinMove computation/NyWindowing.");
                }

                if (dayMinMove < MinDayTradeable)
                    continue;

                double entry = rec.Entry;
                if (entry <= 0.0 || double.IsNaN(entry) || double.IsInfinity(entry))
                    throw new InvalidOperationException($"[hourly] Invalid rec.Entry={entry} for entry={rec.Causal.EntryUtc.Value:O}.");

                bool strongSignal = rec.PredLabel == 0 || rec.PredLabel == 2;

                DateTime start = rec.Causal.EntryUtc.Value;

                DateTime end;
                try
                {
                    end = NyWindowing.ComputeBaselineExitUtc(rec.Causal.EntryUtc, nyTz).Value;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[hourly] Failed to compute baseline-exit for entry={start:O}. " +
                        "Weekend entries must not exist here by contract.",
                        ex);
                }

                int startIdx = LowerBoundOpenTimeUtc(candles1h, start);
                int endIdxExclusive = LowerBoundOpenTimeUtc(candles1h, end);
                if (endIdxExclusive <= startIdx)
                    continue;

                report.Trades++;

                ComputeTpSlPct(dayMinMove, strongSignal, out double tpPct, out double slPct);

                double tpPrice, slPrice;
                if (goLong)
                {
                    tpPrice = entry * (1.0 + tpPct);
                    slPrice = entry * (1.0 - slPct);
                }
                else
                {
                    tpPrice = entry * (1.0 - tpPct);
                    slPrice = entry * (1.0 + slPct);
                }

                bool hitTp = false;
                bool hitSl = false;
                bool isAmb = false;

                for (int i = startIdx; i < endIdxExclusive; i++)
                {
                    var bar = candles1h[i];

                    if (goLong)
                    {
                        bool tpInBar = bar.High >= tpPrice;
                        bool slInBar = bar.Low <= slPrice;

                        if (tpInBar && slInBar) { isAmb = true; break; }
                        if (tpInBar) { hitTp = true; break; }
                        if (slInBar) { hitSl = true; break; }
                    }
                    else
                    {
                        bool tpInBar = bar.Low <= tpPrice;
                        bool slInBar = bar.High >= slPrice;

                        if (tpInBar && slInBar) { isAmb = true; break; }
                        if (tpInBar) { hitTp = true; break; }
                        if (slInBar) { hitSl = true; break; }
                    }
                }

                if (isAmb)
                {
                    report.Ambiguous++;
                    continue;
                }

                double tradeRet;
                if (hitTp)
                {
                    report.TpFirst++;
                    tradeRet = tpPct;
                }
                else if (hitSl)
                {
                    report.SlFirst++;
                    tradeRet = -slPct;
                }
                else
                {
                    double closePrice = candles1h[endIdxExclusive - 1].Close;

                    if (goLong)
                        tradeRet = (closePrice - entry) / entry;
                    else
                        tradeRet = (entry - closePrice) / entry;
                }

                equity *= 1.0 + tradeRet;
                if (equity > peak) peak = equity;

                double dd = (peak - equity) / peak;
                if (dd > maxDd) maxDd = dd;
            }

            report.TotalPnlMultiplier = equity;
            report.TotalPnlPct = (equity - 1.0) * 100.0;
            report.MaxDrawdownPct = maxDd * 100.0;

            return report;
        }

        private static void ComputeTpSlPct(double dayMinMove, bool strongSignal, out double tpPct, out double slPct)
        {
            if (strongSignal)
            {
                tpPct = Math.Max(StrongTpFloor, dayMinMove * StrongTpMul);
                slPct = Math.Max(StrongSlFloor, dayMinMove * StrongSlMul);
            }
            else
            {
                tpPct = Math.Max(WeakTpFloor, dayMinMove * WeakTpMul);
                slPct = Math.Max(WeakSlFloor, dayMinMove * WeakSlMul);
            }
        }

        private static int LowerBoundOpenTimeUtc(IReadOnlyList<Candle1h> all, DateTime t)
        {
            int lo = 0;
            int hi = all.Count;

            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (all[mid].OpenTimeUtc < t) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private static void EnsureStrictlyAscendingUtc<T>(IReadOnlyList<T> list, Func<T, DateTime> key, string name)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (key == null) throw new ArgumentNullException(nameof(key));

            for (int i = 1; i < list.Count; i++)
            {
                var prev = key(list[i - 1]);
                var cur = key(list[i]);

                if (cur <= prev)
                {
                    throw new InvalidOperationException(
                        $"[{name}] Series must be strictly ascending by time. " +
                        $"i={i}, prev={prev:O}, cur={cur:O}.");
                }
            }
        }
    }
}

