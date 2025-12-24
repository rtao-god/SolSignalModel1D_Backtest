using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// UTC entry timestamp, гарантированно принадлежащий NY trading day (не Sat/Sun по NY локальному календарю).
    ///
    /// Инварианты:
    /// - Value.Kind == Utc;
    /// - default(NyTradingEntryUtc) запрещён к использованию (fail-fast при чтении Value);
    /// - создать можно только через NyWindowing.TryCreate*/Create* (weekend по типу недоступен).
    /// </summary>
    public readonly struct NyTradingEntryUtc : IEquatable<NyTradingEntryUtc>, IComparable<NyTradingEntryUtc>
    {
        private readonly DateTime _value;

        public bool IsDefault => _value == default;

        public DateTime Value
        {
            get
            {
                if (_value == default)
                    throw new InvalidOperationException("[nyEntryUtc] NyTradingEntryUtc is default (uninitialized).");

                return _value;
            }
        }

        internal NyTradingEntryUtc(DateTime utc)
        {
            if (utc == default)
                throw new ArgumentException("nyEntryUtc must be initialized (non-default).", nameof(utc));

            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"nyEntryUtc must be UTC. Got Kind={utc.Kind}.", nameof(utc));

            _value = utc;
        }

        public EntryUtc AsEntryUtc() => new EntryUtc(Value);

        public DayKeyUtc DayKeyUtc => DayKeyUtc.FromUtcMomentOrThrow(Value);

        public int CompareTo(NyTradingEntryUtc other) => Value.CompareTo(other.Value);
        public bool Equals(NyTradingEntryUtc other) => _value.Equals(other._value);
        public override bool Equals(object? obj) => obj is NyTradingEntryUtc other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(NyTradingEntryUtc a, NyTradingEntryUtc b) => a.Equals(b);
        public static bool operator !=(NyTradingEntryUtc a, NyTradingEntryUtc b) => !a.Equals(b);

        public static bool operator <(NyTradingEntryUtc a, NyTradingEntryUtc b) => a.Value < b.Value;
        public static bool operator >(NyTradingEntryUtc a, NyTradingEntryUtc b) => a.Value > b.Value;
        public static bool operator <=(NyTradingEntryUtc a, NyTradingEntryUtc b) => a.Value <= b.Value;
        public static bool operator >=(NyTradingEntryUtc a, NyTradingEntryUtc b) => a.Value >= b.Value;

        public override string ToString() => Value.ToString("O");
    }
}
