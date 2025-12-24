using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// Единая сегментация Train/OOS по контракту baseline-exit:
    /// - TRAIN: baseline-exit <= trainUntilUtc
    /// - OOS  : baseline-exit >  trainUntilUtc
    /// - EXCLUDED: baseline-exit не определён по контракту (weekend entry в NY)
    /// </summary>
    public static class NyTrainSplit
    {
        public sealed class Result<T>
        {
            public List<T> Train { get; }
            public List<T> Oos { get; }
            public List<T> Excluded { get; }

            public Result(List<T> train, List<T> oos, List<T> excluded)
            {
                Train = train ?? throw new ArgumentNullException(nameof(train));
                Oos = oos ?? throw new ArgumentNullException(nameof(oos));
                Excluded = excluded ?? throw new ArgumentNullException(nameof(excluded));
            }
        }

        public static string ToIsoDate(DateTime tUtc)
        {
            if (tUtc == default)
                return "0001-01-01";
            if (tUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"[time] expected UTC, got Kind={tUtc.Kind}, t={tUtc:O}.", nameof(tUtc));
            return tUtc.ToString("yyyy-MM-dd");
        }

        public static Result<T> SplitByBaselineExit<T>(
            IReadOnlyList<T> ordered,
            Func<T, EntryUtc> entrySelector,
            DateTime trainUntilUtc,
            TimeZoneInfo nyTz)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));
            if (entrySelector == null) throw new ArgumentNullException(nameof(entrySelector));
            if (trainUntilUtc == default)
                throw new ArgumentException("trainUntilUtc must be initialized (non-default).", nameof(trainUntilUtc));
            if (trainUntilUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof(trainUntilUtc));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var train = new List<T>(ordered.Count);
            var oos = new List<T>(Math.Min(ordered.Count, 256));
            var excluded = new List<T>(Math.Min(ordered.Count, 64));

            for (int i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                var entryUtc = entrySelector(r);

                if (!NyWindowing.TryComputeBaselineExitUtc(entryUtc, nyTz, out var exitUtc))
                {
                    excluded.Add(r);
                    continue;
                }

                // baseline-exit <= trainUntil -> TRAIN, иначе OOS.
                if (exitUtc.Value <= trainUntilUtc) train.Add(r);
                else oos.Add(r);
            }

            return new Result<T>(train, oos, excluded);
        }
    }
}
