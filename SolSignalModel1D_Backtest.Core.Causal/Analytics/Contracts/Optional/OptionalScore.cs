namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts
{
    /// <summary>
    /// Опциональная вероятность/скор с причиной отсутствия.
    /// </summary>
    public readonly struct OptionalScore
    {
        public bool HasValue { get; }
        public double Value { get; }
        public string? MissingReason { get; }

        private OptionalScore(bool hasValue, double value, string? missingReason)
        {
            HasValue = hasValue;
            Value = value;
            MissingReason = missingReason;
        }

        public static OptionalScore Present(double value)
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "OptionalScore requires finite value.");

            return new OptionalScore(true, value, null);
        }

        public static OptionalScore Missing(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("OptionalScore.Missing requires non-empty reason.", nameof(reason));

            return new OptionalScore(false, default, reason);
        }

        public double GetValueOrThrow(string? context = null)
        {
            if (!HasValue)
            {
                string prefix = string.IsNullOrWhiteSpace(context) ? "[optional-score]" : context;
                string reason = MissingReason ?? "<no-reason>";
                throw new InvalidOperationException($"{prefix} Missing score: reason={reason}.");
            }

            return Value;
        }

        public override string ToString()
            => HasValue ? Value.ToString("0.####") : $"<missing:{MissingReason}>";
    }
}
