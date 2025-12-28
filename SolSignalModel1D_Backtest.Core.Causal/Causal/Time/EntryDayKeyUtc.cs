using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
{
    /// <summary>
    /// Day-key дня входа (идентичность дня записи).
    /// Инварианты:
    /// - Value.Kind == Utc;
    /// - Value = 00:00 UTC;
    /// - default запрещён для доменной логики (fail-fast при чтении Value).
    /// </summary>
    public readonly struct EntryDayKeyUtc : IEquatable<EntryDayKeyUtc>, IComparable<EntryDayKeyUtc>
    {
        private readonly DateTime _valueUtc00;

        public bool IsDefault => _valueUtc00 == default;

        public DateTime Value
        {
            get
            {
                if (_valueUtc00 == default)
                    throw new InvalidOperationException("[entry-day] EntryDayKeyUtc is default (uninitialized).");
                return _valueUtc00;
            }
        }

        public string IsoDate => Value.ToString("yyyy-MM-dd");

        public EntryDayKeyUtc(DateTime utcDayStart)
        {
            if (utcDayStart == default)
                throw new ArgumentException("EntryDayKeyUtc must be initialized (non-default).", nameof(utcDayStart));

            if (utcDayStart.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"EntryDayKeyUtc must be UTC. Got Kind={utcDayStart.Kind}, t={utcDayStart:O}.", nameof(utcDayStart));

            if (utcDayStart.TimeOfDay != TimeSpan.Zero)
                throw new ArgumentException($"EntryDayKeyUtc must be UTC day-start (00:00). Got t={utcDayStart:O}.", nameof(utcDayStart));

            _valueUtc00 = utcDayStart;
        }

        public static EntryDayKeyUtc FromUtcDayStartOrThrow(DateTime utcDayStart)
            => new EntryDayKeyUtc(utcDayStart);

        public static EntryDayKeyUtc FromUtcMomentOrThrow(DateTime utcMoment)
        {
            if (utcMoment == default)
                throw new ArgumentException("EntryDayKeyUtc moment must be initialized (non-default).", nameof(utcMoment));

            if (utcMoment.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"EntryDayKeyUtc moment must be UTC. Got Kind={utcMoment.Kind}, t={utcMoment:O}.", nameof(utcMoment));

            var day = DateTime.SpecifyKind(utcMoment.Date, DateTimeKind.Utc);
            return new EntryDayKeyUtc(day);
        }

        public static EntryDayKeyUtc FromEntryUtcOrThrow(EntryUtc entryUtc)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            return FromUtcMomentOrThrow(entryUtc.Value);
        }

        public override string ToString() => IsoDate;

        public bool Equals(EntryDayKeyUtc other) => _valueUtc00.Equals(other._valueUtc00);
        public override bool Equals(object? obj) => obj is EntryDayKeyUtc other && Equals(other);
        public override int GetHashCode() => _valueUtc00.GetHashCode();

        public int CompareTo(EntryDayKeyUtc other) => Value.CompareTo(other.Value);

        public static bool operator ==(EntryDayKeyUtc a, EntryDayKeyUtc b) => a.Equals(b);
        public static bool operator !=(EntryDayKeyUtc a, EntryDayKeyUtc b) => !a.Equals(b);

        public static bool operator <(EntryDayKeyUtc a, EntryDayKeyUtc b) => a.Value < b.Value;
        public static bool operator <=(EntryDayKeyUtc a, EntryDayKeyUtc b) => a.Value <= b.Value;
        public static bool operator >(EntryDayKeyUtc a, EntryDayKeyUtc b) => a.Value > b.Value;
        public static bool operator >=(EntryDayKeyUtc a, EntryDayKeyUtc b) => a.Value >= b.Value;
    }
}
