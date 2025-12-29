namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Time
{
    /// <summary>
    /// UTC entry timestamp: реальный момент входа/решения (не day-key).
    /// Инварианты:
    /// - Value.Kind == Utc
    /// - default(EntryUtc) запрещён к использованию (fail-fast при чтении Value)
    /// </summary>
    public readonly struct EntryUtc : IEquatable<EntryUtc>, IComparable<EntryUtc>
    {
        private readonly UtcInstant _instant;

        public bool IsDefault => _instant.IsDefault;

        public DateTime Value
        {
            get
            {
                if (IsDefault)
                    throw new InvalidOperationException("[entryUtc] EntryUtc is default (uninitialized).");
                return _instant.Value;
            }
        }

        public EntryUtc(DateTime utc)
        {
            _instant = new UtcInstant(utc);
        }

        public EntryUtc(UtcInstant instant)
        {
            if (instant.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(instant));

            _instant = instant;
        }

        public static EntryUtc FromUtcOrThrow(DateTime utc) => new EntryUtc(utc);

        /// <summary>Явная проекция entry на entry-day-key (00:00Z дня входа).</summary>
        public EntryDayKeyUtc EntryDayKeyUtc => EntryDayKeyUtc.FromUtcMomentOrThrow(Value);

        public int CompareTo(EntryUtc other) => Value.CompareTo(other.Value);
        public bool Equals(EntryUtc other) => _instant.Equals(other._instant);
        public override bool Equals(object? obj) => obj is EntryUtc other && Equals(other);
        public override int GetHashCode() => _instant.GetHashCode();

        public static bool operator ==(EntryUtc a, EntryUtc b) => a.Equals(b);
        public static bool operator !=(EntryUtc a, EntryUtc b) => !a.Equals(b);

        public static bool operator <(EntryUtc a, EntryUtc b) => a.Value < b.Value;
        public static bool operator >(EntryUtc a, EntryUtc b) => a.Value > b.Value;
        public static bool operator <=(EntryUtc a, EntryUtc b) => a.Value <= b.Value;
        public static bool operator >=(EntryUtc a, EntryUtc b) => a.Value >= b.Value;

        public override string ToString() => Value.ToString("O");
    }
}
