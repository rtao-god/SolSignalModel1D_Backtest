using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// Day-key (00:00 UTC) границы trainUntil в терминах baseline-exit.
    /// Нужен, чтобы не смешивать "day записи" и "day baseline-exit".
    /// </summary>
    public readonly struct ExitDayKeyUtc : IEquatable<ExitDayKeyUtc>, IComparable<ExitDayKeyUtc>
    {
        private readonly DateTime _valueUtc00;

        public bool IsDefault => _valueUtc00 == default;

        public DateTime Value
        {
            get
            {
                if (_valueUtc00 == default)
                    throw new InvalidOperationException("[exit-day] ExitDayKeyUtc is default (uninitialized).");
                return _valueUtc00;
            }
        }

        public string IsoDate => Value.ToString("yyyy-MM-dd");

        private ExitDayKeyUtc(DateTime utc00)
        {
            if (utc00 == default)
                throw new ArgumentException("exitDayKeyUtc must be initialized (non-default).", nameof(utc00));
            if (utc00.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"exitDayKeyUtc must be UTC. Got Kind={utc00.Kind}, t={utc00:O}.", nameof(utc00));
            if (utc00.TimeOfDay != TimeSpan.Zero)
                throw new ArgumentException($"exitDayKeyUtc must be a day-key (00:00Z). Got t={utc00:O}.", nameof(utc00));

            _valueUtc00 = utc00;
        }

        public static ExitDayKeyUtc FromUtcOrThrow(DateTime utc00) => new ExitDayKeyUtc(utc00);

        /// <summary>
        /// Явная проекция baseline-exit UTC момента на exit-day-key (00:00Z).
        /// Название фиксирует семантику: это НЕ entry-day-key.
        /// </summary>
        public static ExitDayKeyUtc FromBaselineExitUtcOrThrow(DateTime baselineExitUtc)
        {
            if (baselineExitUtc == default)
                throw new ArgumentException("baselineExitUtc must be initialized (non-default).", nameof(baselineExitUtc));
            if (baselineExitUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"baselineExitUtc must be UTC. Got Kind={baselineExitUtc.Kind}, t={baselineExitUtc:O}.", nameof(baselineExitUtc));

            var dayUtc00 = DateTime.SpecifyKind(baselineExitUtc.Date, DateTimeKind.Utc);
            return new ExitDayKeyUtc(dayUtc00);
        }

        public static ExitDayKeyUtc FromUtcMomentOrThrow(DateTime utcMoment)
        {
            if (utcMoment == default)
                throw new ArgumentException("utcMoment must be initialized (non-default).", nameof(utcMoment));
            if (utcMoment.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"utcMoment must be UTC. Got Kind={utcMoment.Kind}, t={utcMoment:O}.", nameof(utcMoment));

            var dayUtc00 = DateTime.SpecifyKind(utcMoment.Date, DateTimeKind.Utc);
            return new ExitDayKeyUtc(dayUtc00);
        }

        public int CompareTo(ExitDayKeyUtc other) => Value.CompareTo(other.Value);
        public bool Equals(ExitDayKeyUtc other) => _valueUtc00.Equals(other._valueUtc00);
        public override bool Equals(object? obj) => obj is ExitDayKeyUtc other && Equals(other);
        public override int GetHashCode() => _valueUtc00.GetHashCode();

        public static bool operator ==(ExitDayKeyUtc a, ExitDayKeyUtc b) => a.Equals(b);
        public static bool operator !=(ExitDayKeyUtc a, ExitDayKeyUtc b) => !a.Equals(b);

        public static bool operator <(ExitDayKeyUtc a, ExitDayKeyUtc b) => a.Value < b.Value;
        public static bool operator >(ExitDayKeyUtc a, ExitDayKeyUtc b) => a.Value > b.Value;
        public static bool operator <=(ExitDayKeyUtc a, ExitDayKeyUtc b) => a.Value <= b.Value;
        public static bool operator >=(ExitDayKeyUtc a, ExitDayKeyUtc b) => a.Value >= b.Value;

        public override string ToString() => IsoDate;
    }
}
