using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// UTC instant. Инварианты: Kind=Utc
    /// </summary>
    public readonly record struct UtcInstant
    {
        public DateTime Value { get; }

        public UtcInstant(DateTime value)
        {
            if (value == default)
                throw new ArgumentException("UTC instant must be non-default.", nameof(value));
            if (value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("UTC instant must have DateTimeKind.Utc.", nameof(value));

            Value = value;
        }

        public override string ToString() => Value.ToString("O");
    }

    /// <summary>
    /// Момент принятия решения/входа.
    /// </summary>
    public readonly record struct EntryUtc(UtcInstant Instant)
    {
        public DateTime Value => Instant.Value;
        public override string ToString() => Instant.ToString();
    }

    /// <summary>
    /// Момент baseline-exit, вычисленный из EntryUtc по NY-контракту.
    /// </summary>
    public readonly record struct BaselineExitUtc(UtcInstant Instant)
    {
        public DateTime Value => Instant.Value;
        public override string ToString() => Instant.ToString();
    }

    /// <summary>
    /// Ключ торгового дня в NY. Не является моментом времени.
    /// </summary>
    public readonly record struct NyTradingDay(DateOnly Value)
    {
        public override string ToString() => Value.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Каузальный штамп записи: EntryUtc + NyDay + BaselineExitUtc.
    /// Инвариант: BaselineExitUtc определён (weekend-строки не попадают в causal).
    /// </summary>
    public readonly record struct CausalStamp(
        EntryUtc EntryUtc,
        NyTradingDay NyDay,
        BaselineExitUtc BaselineExitUtc
        );
}
