using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// Split train/OOS по baseline-exit(entryUtc) относительно TrainUntilUtc.
    /// Weekend-entry не имеет baseline-exit по контракту и попадает в Excluded.
    /// </summary>
    public static class TrainSplitByBaselineExit
    {
        public sealed class SplitResult<T>
        {
            public required IReadOnlyList<T> Train { get; init; }
            public required IReadOnlyList<T> Oos { get; init; }
            public required IReadOnlyList<T> Excluded { get; init; }
        }

        public static SplitResult<T> Split<T>(
            IReadOnlyList<T> items,
            Func<T, EntryUtc> entrySelector,
            TrainUntilUtc trainUntilUtc,
            TimeZoneInfo nyTz)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (entrySelector == null) throw new ArgumentNullException(nameof(entrySelector));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var train = new List<T>(items.Count);
            var oos = new List<T>(Math.Min(items.Count, 256));
            var excluded = new List<T>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var entryUtc = entrySelector(item);

                if (!NyWindowing.TryComputeBaselineExitUtc(entryUtc, nyTz, out var exitUtc))
                {
                    excluded.Add(item);
                    continue;
                }

                if (exitUtc.Value <= trainUntilUtc.Value)
                    train.Add(item);
                else
                    oos.Add(item);
            }

            return new SplitResult<T>
            {
                Train = train,
                Oos = oos,
                Excluded = excluded
            };
        }
    }
}
