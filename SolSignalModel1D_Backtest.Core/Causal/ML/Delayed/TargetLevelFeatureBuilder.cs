using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Delayed
{
    /// <summary>
    /// Фичи для delayed-моделей (A/B) на момент входа.
    /// Строго каузально: используем только SOL 1h в окне [entryUtc-6h, entryUtc).
    /// </summary>
    public static class TargetLevelFeatureBuilder
    {
        public static float[] Build(
            EntryUtc entryUtc,
            bool goLong,
            bool strongSignal,
            double dayMinMove,
            double entryPrice,
            IReadOnlyList<Candle1h>? candles1h)
        {
            if (entryUtc.IsDefault)
                throw new InvalidOperationException("[delayed-feats] entryUtc is default (uninitialized).");

            return Build(
                entryUtc: entryUtc.Value,
                goLong: goLong,
                strongSignal: strongSignal,
                dayMinMove: dayMinMove,
                entryPrice: entryPrice,
                candles1h: candles1h);
        }

        public static float[] Build(
            DateTime entryUtc,
            bool goLong,
            bool strongSignal,
            double dayMinMove,
            double entryPrice,
            IReadOnlyList<Candle1h>? candles1h)
        {
            var feats = new float[MlSchema.FeatureCount];

            feats[0] = goLong ? 1f : 0f;
            feats[1] = strongSignal ? 1f : 0f;
            feats[2] = (float)dayMinMove;

            if (entryUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[delayed-feats] entryUtc must be UTC: {entryUtc:O}.");

            if (candles1h == null)
                throw new InvalidOperationException("[delayed-feats] candles1h is null. Data loader contract is broken.");

            if (candles1h.Count == 0)
                throw new InvalidOperationException("[delayed-feats] candles1h is empty. Data loader contract is broken.");

            if (double.IsNaN(entryPrice) || double.IsInfinity(entryPrice) || entryPrice <= 0.0)
                throw new InvalidOperationException($"[delayed-feats] invalid entryPrice={entryPrice} at {entryUtc:O}.");

            DateTime from = entryUtc.AddHours(-6);

            var last6 = candles1h
                .Where(c => c.OpenTimeUtc < entryUtc && c.OpenTimeUtc >= from)
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            if (last6.Count < 2)
                throw new InvalidOperationException(
                    $"[delayed-feats] insufficient 1h history in [{from:O}, {entryUtc:O}). count={last6.Count}.");

            var lastBar = last6[last6.Count - 1];

            if (double.IsNaN(lastBar.Close) || double.IsInfinity(lastBar.Close) || lastBar.Close <= 0.0)
                throw new InvalidOperationException(
                    $"[delayed-feats] invalid lastBar.Close={lastBar.Close} at {lastBar.OpenTimeUtc:O} (entry={entryUtc:O}).");

            double closeNow = lastBar.Close;

            Candle1h GetByOffsetFromEnd(int offset)
            {
                int idx = last6.Count - 1 - offset;
                if (idx < 0)
                    throw new InvalidOperationException(
                        $"[delayed-feats] not enough bars for offset={offset}. last6.Count={last6.Count} entry={entryUtc:O}.");

                return last6[idx];
            }

            var c1 = GetByOffsetFromEnd(1);
            var c3 = GetByOffsetFromEnd(3);
            var c6 = GetByOffsetFromEnd(5);

            double Ret(double fromClose, string tag, DateTime t)
            {
                if (double.IsNaN(fromClose) || double.IsInfinity(fromClose) || fromClose <= 0.0)
                    throw new InvalidOperationException(
                        $"[delayed-feats] invalid {tag}.Close={fromClose} at {t:O} (entry={entryUtc:O}).");

                return closeNow / fromClose - 1.0;
            }

            double ret1h = Ret(c1.Close, "c1", c1.OpenTimeUtc);
            double ret3h = Ret(c3.Close, "c3", c3.OpenTimeUtc);
            double ret6h = Ret(c6.Close, "c6", c6.OpenTimeUtc);

            feats[3] = (float)ret1h;
            feats[4] = (float)ret3h;
            feats[5] = (float)ret6h;

            var last3 = last6.Count <= 3
                ? last6
                : last6.Skip(last6.Count - 3).ToList();

            if (last3.Count < 1)
                throw new InvalidOperationException($"[delayed-feats] last3 is empty at entry={entryUtc:O}.");

            double high3 = last3.Max(h => h.High);
            double low3 = last3.Min(h => h.Low);
            double high6 = last6.Max(h => h.High);
            double low6 = last6.Min(h => h.Low);

            if (high3 <= 0.0 || low3 <= 0.0 || high6 <= 0.0 || low6 <= 0.0)
                throw new InvalidOperationException(
                    $"[delayed-feats] invalid high/low in history window at entry={entryUtc:O}.");

            if (high3 < low3 || high6 < low6)
                throw new InvalidOperationException(
                    $"[delayed-feats] corrupted OHLC: high < low at entry={entryUtc:O}.");

            double range3 = (high3 - low3) / closeNow;
            double range6 = (high6 - low6) / closeNow;

            feats[6] = (float)range3;
            feats[7] = (float)range6;

            double span3 = Math.Max(high3 - low3, 1e-9);
            double span6 = Math.Max(high6 - low6, 1e-9);

            double retr3Up = (closeNow - low3) / span3;
            double retr3Down = (high3 - closeNow) / span3;
            double retr6Up = (closeNow - low6) / span6;
            double retr6Down = (high6 - closeNow) / span6;

            retr3Up = Math.Clamp(retr3Up, 0.0, 1.0);
            retr3Down = Math.Clamp(retr3Down, 0.0, 1.0);
            retr6Up = Math.Clamp(retr6Up, 0.0, 1.0);
            retr6Down = Math.Clamp(retr6Down, 0.0, 1.0);

            feats[8] = (float)retr3Up;
            feats[9] = (float)retr3Down;
            feats[10] = (float)retr6Up;
            feats[11] = (float)retr6Down;

            double sumSq = 0.0;
            int steps = 0;

            for (int i = 1; i < last6.Count; i++)
            {
                double prev = last6[i - 1].Close;
                double cur = last6[i].Close;

                if (prev <= 0.0 || cur <= 0.0 || double.IsNaN(prev) || double.IsNaN(cur) || double.IsInfinity(prev) || double.IsInfinity(cur))
                    throw new InvalidOperationException(
                        $"[delayed-feats] invalid Close in vol window at entry={entryUtc:O}. prev={prev}, cur={cur}.");

                double lr = Math.Log(cur / prev);
                sumSq += lr * lr;
                steps++;
            }

            if (steps <= 0)
                throw new InvalidOperationException($"[delayed-feats] steps=0 in vol calc at entry={entryUtc:O}.");

            double vol6h = Math.Sqrt(sumSq);
            feats[12] = (float)vol6h;

            feats[13] = entryUtc.Hour / 23f;

            return feats;
        }
    }
}
