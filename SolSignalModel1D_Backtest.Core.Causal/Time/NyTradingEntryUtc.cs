using System;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
{
    /// <summary>
    /// Trusted ctor token (сигнатурный барьер):
    /// - нужен, чтобы legacy-конструктор NyTradingEntryUtc(DateTime) можно было запретить (Obsolete error),
    ///   а NyWindowing создавал значения через отдельную сигнатуру.
    ///
    /// Ограничение C#: внутри одной сборки этот токен теоретически доступен и другим типам (internal),
    /// но legacy "new NyTradingEntryUtc(utc)" мы убиваем компиляционно.
    /// </summary>
    internal readonly struct NyTradingEntryUtcTrustedCtorToken { }

    /// <summary>
    /// UTC entry timestamp, гарантированно являющийся NY morning торгового дня
    /// (не Sat/Sun по NY локальному календарю и строго 07:00 зимой / 08:00 летом по NY).
    ///
    /// Инварианты:
    /// - Value.Kind == Utc;
    /// - default(NyTradingEntryUtc) запрещён к использованию (fail-fast при чтении Value);
    /// - доменная валидация (не-weekend и NY-morning) выполняется через NyWindowing.TryCreate*/Create*.
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

        /// <summary>
        /// Запрещаем legacy-конструкцию "new NyTradingEntryUtc(utc)" компиляционно.
        /// Единственный нормальный путь: NyWindowing.TryCreateNyTradingEntryUtc / CreateNyTradingEntryUtcOrThrow.
        /// </summary>
        [Obsolete("Use NyWindowing.TryCreateNyTradingEntryUtc/CreateNyTradingEntryUtcOrThrow.", error: true)]
        public NyTradingEntryUtc(DateTime utc)
        {
            throw new NotSupportedException("Use NyWindowing.TryCreateNyTradingEntryUtc/CreateNyTradingEntryUtcOrThrow.");
        }

        internal NyTradingEntryUtc(DateTime utc, NyTradingEntryUtcTrustedCtorToken _)
        {
            if (utc == default)
                throw new ArgumentException("nyEntryUtc must be initialized (non-default).", nameof(utc));

            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"nyEntryUtc must be UTC. Got Kind={utc.Kind}.", nameof(utc));

            _value = utc;
        }

        public EntryUtc AsEntryUtc() => new EntryUtc(Value);

        public EntryDayKeyUtc EntryDayKeyUtc => EntryDayKeyUtc.FromUtcMomentOrThrow(Value);

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

