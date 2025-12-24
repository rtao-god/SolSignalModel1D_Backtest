using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// Семантический момент времени: граница trainUntil в терминах baseline-exit (UTC).
    /// Инварианты:
    /// - Value.Kind == Utc;
    /// - Value != default.
    /// </summary>
    public readonly record struct TrainUntilUtc
    {
        public DateTime Value { get; }

        public TrainUntilUtc(DateTime valueUtc)
        {
            if (valueUtc == default)
                throw new ArgumentException("trainUntilUtc must not be default(DateTime).", nameof(valueUtc));

            if (valueUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException(
                    $"trainUntilUtc must be UTC. Got Kind={valueUtc.Kind}, t={valueUtc:O}.",
                    nameof(valueUtc));

            Value = valueUtc;
        }

        public string IsoDate => Value.ToString("yyyy-MM-dd");
    }
}
