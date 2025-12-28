using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
{
    public static class NyWindowingTestSeriesUtils
    {
        public static List<Candle6h> FilterNyMorningOnly(IReadOnlyList<Candle6h> candles, TimeZoneInfo nyTz)
        {
            if (candles == null) throw new ArgumentNullException(nameof(candles));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var result = new List<Candle6h>(candles.Count);

            for (int i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                if (c.OpenTimeUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[test] Candle6h.OpenTimeUtc must be UTC. Got {c.OpenTimeUtc.Kind} at i={i}: {c.OpenTimeUtc:O}.");

                var entry = new EntryUtc(new UtcInstant(c.OpenTimeUtc));
                if (NyWindowing.IsNyMorning(entry, nyTz))
                    result.Add(c);
            }

            return result;
        }

        public static List<T> BuildSpacedTest<T>(
            IReadOnlyList<T> ordered,
            int take,
            int skip,
            int blocks,
            Func<T, DateTime> dateSelector)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));
            if (dateSelector == null) throw new ArgumentNullException(nameof(dateSelector));
            if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take), take, "take must be > 0.");
            if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip), skip, "skip must be >= 0.");
            if (blocks <= 0) throw new ArgumentOutOfRangeException(nameof(blocks), blocks, "blocks must be > 0.");

            DateTime? prev = null;
            for (int i = 0; i < ordered.Count; i++)
            {
                var t = dateSelector(ordered[i]);
                if (prev.HasValue && t < prev.Value)
                    throw new InvalidOperationException($"[test] ordered must be ascending by date. i={i}, prev={prev.Value:O}, cur={t:O}.");
                prev = t;
            }

            var picked = new List<T>(Math.Min(ordered.Count, take * blocks));
            int endExclusive = ordered.Count;

            for (int b = 0; b < blocks && endExclusive > 0; b++)
            {
                int start = Math.Max(0, endExclusive - take);
                for (int i = start; i < endExclusive; i++)
                    picked.Add(ordered[i]);

                endExclusive = start - skip;
            }

            return picked.OrderBy(dateSelector).ToList();
        }

        public static IEnumerable<List<T>> GroupByBlocks<T>(IReadOnlyList<T> rows, int blockSize)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "blockSize must be > 0.");

            for (int i = 0; i < rows.Count; i += blockSize)
            {
                int n = Math.Min(blockSize, rows.Count - i);
                var block = new List<T>(n);
                for (int j = 0; j < n; j++)
                    block.Add(rows[i + j]);

                yield return block;
            }
        }
    }
}

