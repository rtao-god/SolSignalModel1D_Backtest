using System.Diagnostics;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Diagnostics;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
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

        public static string ToIsoDate(EntryDayKeyUtc dayKeyUtc)
        {
            if (dayKeyUtc.IsDefault)
                throw new ArgumentException("dayKeyUtc must be initialized (non-default).", nameof(dayKeyUtc));

            return dayKeyUtc.Value.ToString("yyyy-MM-dd");
        }

        public static string ToIsoDate(ExitDayKeyUtc dayKeyUtc)
        {
            if (dayKeyUtc.IsDefault)
                throw new ArgumentException("dayKeyUtc must be initialized (non-default).", nameof(dayKeyUtc));

            return dayKeyUtc.Value.ToString("yyyy-MM-dd");
        }

        public static string ToIsoDate(TrainUntilExitDayKeyUtc dayKeyUtc)
        {
            if (dayKeyUtc.IsDefault)
                throw new ArgumentException("dayKeyUtc must be initialized (non-default).", nameof(dayKeyUtc));

            return dayKeyUtc.Value.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Каноничная классификация: entryUtc + граница ExitDayKeyUtc -> Train/Oos/Excluded.
        /// </summary>
        public static EntryClass ClassifyByBaselineExit(
            EntryUtc entryUtc,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz,
            out ExitDayKeyUtc baselineExitDayKeyUtc)
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

            if (LeakageSwitches.IsEnabled(LeakageMode.SplitUseEntryDayKey))
            {
                baselineExitDayKeyUtc = ExitDayKeyUtc.FromUtcMomentOrThrow(entryUtc.Value);
                return baselineExitDayKeyUtc.Value <= trainUntilExitDayKeyUtc.Value
                    ? EntryClass.Train
                    : EntryClass.Oos;
            }

            baselineExitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value);

            return baselineExitDayKeyUtc.Value <= trainUntilExitDayKeyUtc.Value
                ? EntryClass.Train
                : EntryClass.Oos;
        }

        /// <summary>
        /// Каноничный сплит: по baseline-exit day-key (ExitDayKeyUtc), а не по entry-day-key.
        /// ordered обязан быть строго возрастающим по entrySelector(...).Value (UTC).
        /// </summary>
        public static Split<T> SplitByBaselineExit<T>(
            IReadOnlyList<T> ordered,
            Func<T, EntryUtc> entrySelector,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
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
                    baselineExitDayKeyUtc: out var baselineExitDayKeyUtc);

                var exitDayKeyText = baselineExitDayKeyUtc.IsDefault
                    ? "<default>"
                    : baselineExitDayKeyUtc.Value.ToString("yyyy-MM-dd");

                if (cls == EntryClass.Train)
                {
                    Debug.Assert(
                        !baselineExitDayKeyUtc.IsDefault && baselineExitDayKeyUtc.Value <= trainUntilExitDayKeyUtc.Value,
                        $"[ny-split] Инвариант Train нарушен: exitDayKey={exitDayKeyText}, trainUntilExitDayKey={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}, entryUtc={cur:O}.");
                    train.Add(x);
                }
                else if (cls == EntryClass.Oos)
                {
                    Debug.Assert(
                        !baselineExitDayKeyUtc.IsDefault && baselineExitDayKeyUtc.Value > trainUntilExitDayKeyUtc.Value,
                        $"[ny-split] Инвариант OOS нарушен: exitDayKey={exitDayKeyText}, trainUntilExitDayKey={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}, entryUtc={cur:O}.");
                    oos.Add(x);
                }
                else
                {
                    Debug.Assert(
                        baselineExitDayKeyUtc.IsDefault,
                        $"[ny-split] Инвариант Excluded нарушен: exitDayKey={exitDayKeyText}, entryUtc={cur:O}.");
                    excluded.Add(x);
                }
            }

            return new Split<T>(train, oos, excluded);
        }
    }
}

