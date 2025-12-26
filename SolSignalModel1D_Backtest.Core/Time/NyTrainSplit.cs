using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Time
{
    public static partial class NyTrainSplit
    {
        public enum EntryClass
        {
            Train = 0,
            Oos = 1,
            Excluded = 2
        }

        public sealed class Split<T>
        {
            public IReadOnlyList<T> Train { get; }
            public IReadOnlyList<T> Oos { get; }
            public IReadOnlyList<T> Excluded { get; }

            public Split(IReadOnlyList<T> train, IReadOnlyList<T> oos, IReadOnlyList<T> excluded)
            {
                Train = train ?? throw new ArgumentNullException(nameof(train));
                Oos = oos ?? throw new ArgumentNullException(nameof(oos));
                Excluded = excluded ?? throw new ArgumentNullException(nameof(excluded));
            }
        }

        public static string ToIsoDate(DayKeyUtc dayKeyUtc)
        {
            if (dayKeyUtc.IsDefault)
                throw new ArgumentException("dayKeyUtc must be initialized (non-default).", nameof(dayKeyUtc));

            return dayKeyUtc.Value.ToString("yyyy-MM-dd");
        }

        public static EntryClass ClassifyByBaselineExit(
            EntryUtc entryUtc,
            DayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz,
            out DayKeyUtc baselineExitDayKeyUtc)
        {
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            if (!NyWindowing.TryComputeBaselineExitUtc(entryUtc, nyTz, out var exitUtc))
            {
                baselineExitDayKeyUtc = default;
                return EntryClass.Excluded;
            }

            baselineExitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(exitUtc.Value);

            return baselineExitDayKeyUtc.Value <= trainUntilExitDayKeyUtc.Value
                ? EntryClass.Train
                : EntryClass.Oos;
        }

        /// <summary>
        /// Каноничный сплит: по baseline-exit day-key (а не по entryUtc).
        /// ordered обязан быть строго возрастающим по entrySelector(...).Value (UTC).
        /// </summary>
        public static Split<T> SplitByBaselineExit<T>(
            IReadOnlyList<T> ordered,
            Func<T, EntryUtc> entrySelector,
            DayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));
            if (entrySelector == null) throw new ArgumentNullException(nameof(entrySelector));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            var train = new List<T>(ordered.Count);
            var oos = new List<T>(Math.Max(0, ordered.Count / 3));
            var excluded = new List<T>();

            bool hasPrev = false;
            DateTime prev = default;

            for (int i = 0; i < ordered.Count; i++)
            {
                var x = ordered[i];

                var e = entrySelector(x);
                if (e.IsDefault)
                    throw new InvalidOperationException("[ny-split] entrySelector returned default EntryUtc.");

                var cur = e.Value;
                if (cur.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[ny-split] entrySelector must return UTC. got Kind={cur.Kind}, t={cur:O}.");

                if (hasPrev && cur <= prev)
                    throw new InvalidOperationException($"[ny-split] ordered must be strictly ascending by entryUtc. prev={prev:O}, cur={cur:O}.");

                prev = cur;
                hasPrev = true;

                var cls = ClassifyByBaselineExit(
                    entryUtc: e,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: nyTz,
                    baselineExitDayKeyUtc: out _);

                if (cls == EntryClass.Train) train.Add(x);
                else if (cls == EntryClass.Oos) oos.Add(x);
                else excluded.Add(x);
            }

            return new Split<T>(train, oos, excluded);
        }
    }
}
