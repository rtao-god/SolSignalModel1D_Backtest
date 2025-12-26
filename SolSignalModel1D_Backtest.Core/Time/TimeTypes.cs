using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// UTC instant.
    /// Инварианты:
    /// - Value != default;
    /// - Value.Kind == Utc;
    /// - default(UtcInstant) запрещён к использованию (fail-fast при чтении Value).
    /// </summary>
    public readonly record struct UtcInstant
    {
        private readonly DateTime _value;

        public bool IsDefault => _value == default;

        public DateTime Value
        {
            get
            {
                if (_value == default)
                    throw new InvalidOperationException("[utc] UtcInstant is default (uninitialized).");
                return _value;
            }
        }

        public UtcInstant(DateTime value)
        {
            if (value == default)
                throw new ArgumentException("UTC instant must be non-default.", nameof(value));

            if (value.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"UTC instant must have DateTimeKind.Utc, got {value.Kind}.", nameof(value));

            _value = value;
        }

        public override string ToString() => Value.ToString("O");
    }

    /// <summary>
    /// Baseline-exit moment: конец baseline-окна, вычисляемый строго из EntryUtc (UTC instant).
    /// Инварианты:
    /// - default(BaselineExitUtc) запрещён к использованию (fail-fast при чтении Value/Instant).
    /// </summary>
    public readonly record struct BaselineExitUtc
    {
        private readonly UtcInstant _instant;

        public bool IsDefault => _instant.IsDefault;

        public UtcInstant Instant
        {
            get
            {
                if (_instant.IsDefault)
                    throw new InvalidOperationException("[time] BaselineExitUtc is default (uninitialized).");
                return _instant;
            }
        }

        public DateTime Value => Instant.Value;

        public BaselineExitUtc(UtcInstant instant)
        {
            if (instant.IsDefault)
                throw new ArgumentException("BaselineExitUtc must be non-default.", nameof(instant));

            _instant = instant;
        }

        public override string ToString() => Instant.ToString();
    }

    /// <summary>
    /// NY trading day-key (DateOnly).
    /// Не является моментом времени.
    /// default(NyTradingDay) запрещён к использованию (fail-fast при чтении Value).
    /// </summary>
    public readonly record struct NyTradingDay
    {
        private readonly DateOnly _value;

        public bool IsDefault => _value == default;

        public DateOnly Value
        {
            get
            {
                if (_value == default)
                    throw new InvalidOperationException("[time] NyTradingDay is default (uninitialized).");
                return _value;
            }
        }

        public NyTradingDay(DateOnly value)
        {
            if (value == default)
                throw new ArgumentException("NyTradingDay must be non-default.", nameof(value));

            // Weekend-валидация — обязанность NyWindowing (Try*/OrThrow),
            // здесь держим только "не default" контракт.
            _value = value;
        }

        public override string ToString() => Value.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Causal stamp: EntryUtc + NyTradingDay + BaselineExitUtc.
    /// </summary>
    public readonly record struct CausalStamp(
        EntryUtc EntryUtc,
        NyTradingDay NyDay,
        BaselineExitUtc BaselineExitUtc
    );
}
