using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
{
    /// <summary>
    /// Семантический момент времени: граница trainUntil в терминах baseline-exit (UTC).
    /// Инварианты:
    /// - Value.Kind == Utc;
    /// - Value != default.
    /// </summary>
    public readonly record struct TrainUntilUtc
    {
        private readonly DateTime _value;

        public bool IsDefault => _value == default;

        public DateTime Value
        {
            get
            {
                if (_value == default)
                    throw new InvalidOperationException("[train-until] TrainUntilUtc is default (uninitialized).");
                return _value;
            }
        }

        public TrainUntilUtc(DateTime valueUtc)
        {
            if (valueUtc == default)
                throw new ArgumentException("trainUntilUtc must not be default(DateTime).", nameof(valueUtc));

            if (valueUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException(
                    $"trainUntilUtc must be UTC. Got Kind={valueUtc.Kind}, t={valueUtc:O}.",
                    nameof(valueUtc));

            _value = valueUtc;
        }

        public string IsoDate => Value.ToString("yyyy-MM-dd");

        /// <summary>Exit-day-key (00:00Z) границы trainUntil в терминах baseline-exit.</summary>
        public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc => TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(Value);
    }
}
