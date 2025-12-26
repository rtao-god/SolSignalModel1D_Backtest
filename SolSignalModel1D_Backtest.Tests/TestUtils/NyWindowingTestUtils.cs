using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
{
    public static class NyWindowingTestUtils
    {
        public static TimeZoneInfo NewYorkTz { get; } = ResolveNewYorkTimeZoneOrThrow();

        public static EntryUtc EntryUtcFromUtcOrThrow(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[test] Expected UTC DateTime. Got Kind={utc.Kind}, t={utc:O}.");

            return new EntryUtc(new UtcInstant(utc));
        }

        public static EntryUtc EntryUtcFromNyDayOrThrow(int year, int month, int day)
        {
            var nyDay = new NyTradingDay(new DateOnly(year, month, day));
            return NyWindowing.ComputeEntryUtcFromNyDayOrThrow(nyDay, NewYorkTz);
        }

        public static List<Candle6h> FilterNyMorningOnly(IEnumerable<Candle6h> candles, TimeZoneInfo nyTz)
        {
            if (candles == null) throw new ArgumentNullException(nameof(candles));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var list = new List<Candle6h>();
            foreach (var c in candles)
            {
                if (c == null) continue;

                var entry = EntryUtcFromUtcOrThrow(c.OpenTimeUtc);
                if (NyWindowing.IsNyMorning(entry, nyTz))
                    list.Add(c);
            }

            return list;
        }

        public static List<T> BuildSpacedTest<T>(
            IReadOnlyList<T> rows,
            int take,
            int skip,
            int blocks,
            Func<T, DateTime> dateSelector)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (dateSelector == null) throw new ArgumentNullException(nameof(dateSelector));
            if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
            if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
            if (blocks <= 0) throw new ArgumentOutOfRangeException(nameof(blocks));

            var ordered = rows.OrderBy(dateSelector).ToList();
            if (ordered.Count == 0) return new List<T>();

            var picked = new List<(DateTime dt, T row)>();

            int idx = ordered.Count;
            for (int b = 0; b < blocks && idx > 0; b++)
            {
                int start = Math.Max(0, idx - take);
                for (int i = start; i < idx; i++)
                    picked.Add((dateSelector(ordered[i]), ordered[i]));

                idx = Math.Max(0, start - skip);
            }

            return picked
                .OrderBy(x => x.dt)
                .Select(x => x.row)
                .ToList();
        }

        public static IEnumerable<IReadOnlyList<T>> GroupByBlocks<T>(IReadOnlyList<T> rows, int blockSize)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));

            for (int i = 0; i < rows.Count; i += blockSize)
            {
                int len = Math.Min(blockSize, rows.Count - i);
                var chunk = new List<T>(len);
                for (int j = 0; j < len; j++)
                    chunk.Add(rows[i + j]);

                yield return chunk;
            }
        }

        private static TimeZoneInfo ResolveNewYorkTimeZoneOrThrow()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
        }
    }
}
