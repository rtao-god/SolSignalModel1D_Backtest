using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
{
    public static partial class NyTrainSplit
    {
        public sealed class SplitStrict<T>
        {
            public TrainOnly<T> Train { get; }
            public IReadOnlyList<T> Oos { get; }

            public SplitStrict(TrainOnly<T> train, IReadOnlyList<T> oos)
            {
                Train = train ?? throw new ArgumentNullException(nameof(train));
                Oos = oos ?? throw new ArgumentNullException(nameof(oos));
            }
        }

        public static SplitStrict<T> SplitByBaselineExitStrict<T>(
            IReadOnlyList<T> ordered,
            Func<T, EntryUtc> entrySelector,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz,
            string tag)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));
            if (entrySelector == null) throw new ArgumentNullException(nameof(entrySelector));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("tag must be non-empty.", nameof(tag));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            var split = SplitByBaselineExit(
                ordered: ordered,
                entrySelector: entrySelector,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz);

            if (split.Excluded.Count > 0)
            {
                var sample = split.Excluded
                    .Take(Math.Min(10, split.Excluded.Count))
                    .Select(x =>
                    {
                        var entryUtc = entrySelector(x).Value;
                        var entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, nyTz);
                        return $"{entryUtc:O} (nyLocal={entryLocal:O})";
                    });

                throw new InvalidOperationException(
                    $"[{tag}] Есть исключенные записи (baseline-exit не определен). кол-во={split.Excluded.Count}. " +
                    $"trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}. " +
                    $"пример=[{string.Join(", ", sample)}].");
            }

            var trainOnly = new TrainOnly<T>(
                items: split.Train,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                tag: tag);

            return new SplitStrict<T>(trainOnly, split.Oos);
        }
    }
}
