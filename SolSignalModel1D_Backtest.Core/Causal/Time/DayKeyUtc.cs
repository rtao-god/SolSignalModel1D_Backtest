using System;
using System.Globalization;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// UTC day-key (00:00Z). Используется как стабильная идентичность дня.
    /// Инварианты:
    /// - Value.Kind == Utc
    /// - Value.TimeOfDay == 00:00:00
    /// - default(DayKeyUtc) запрещён к использованию (fail-fast при чтении Value)
    /// </summary>
    public readonly struct DayKeyUtc : IEquatable<DayKeyUtc>, IComparable<DayKeyUtc>, IFormattable
    {
        private readonly DateTime _value;

        public bool IsDefault => _value == default;

        public DateTime Value
        {
            get
            {
                if (_value == default)
                    throw new InvalidOperationException("[day-key] DayKeyUtc is default (uninitialized).");

                return _value;
            }
        }

        public DayKeyUtc(DateTime utc00)
        {
            if (utc00 == default)
                throw new ArgumentException("dayKeyUtc must be initialized (non-default).", nameof(utc00));

            if (utc00.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"dayKeyUtc must be UTC. Got Kind={utc00.Kind}.", nameof(utc00));

            if (utc00.TimeOfDay != TimeSpan.Zero)
                throw new ArgumentException($"dayKeyUtc must be at 00:00Z, got {utc00:O}.", nameof(utc00));

            _value = utc00;
        }

        public static DayKeyUtc FromUtcOrThrow(DateTime utc00) => new DayKeyUtc(utc00);

        /// <summary>
        /// Явная проекция любого UTC момента на day-key (00:00Z) его даты.
        /// </summary>
        public static DayKeyUtc FromUtcMomentOrThrow(DateTime utc)
        {
            if (utc == default)
                throw new ArgumentException("utc must be initialized (non-default).", nameof(utc));

            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"utc must be UTC. Got Kind={utc.Kind}.", nameof(utc));

            var utc00 = DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
            return new DayKeyUtc(utc00);
        }

        public int CompareTo(DayKeyUtc other) => Value.CompareTo(other.Value);
        public bool Equals(DayKeyUtc other) => _value.Equals(other._value);
        public override bool Equals(object? obj) => obj is DayKeyUtc other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(DayKeyUtc a, DayKeyUtc b) => a.Equals(b);
        public static bool operator !=(DayKeyUtc a, DayKeyUtc b) => !a.Equals(b);

        public static bool operator <(DayKeyUtc a, DayKeyUtc b) => a.Value < b.Value;
        public static bool operator >(DayKeyUtc a, DayKeyUtc b) => a.Value > b.Value;
        public static bool operator <=(DayKeyUtc a, DayKeyUtc b) => a.Value <= b.Value;
        public static bool operator >=(DayKeyUtc a, DayKeyUtc b) => a.Value >= b.Value;

        public override string ToString() => Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public string ToString(string? format, IFormatProvider? formatProvider)
            => Value.ToString(format, formatProvider);
    }
}
