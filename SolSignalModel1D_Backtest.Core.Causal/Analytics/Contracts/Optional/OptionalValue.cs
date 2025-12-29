namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts
{
    /// <summary>
    /// Явная "опциональность" значения с причиной отсутствия.
    /// </summary>
    public readonly struct OptionalValue<T>
    {
        public bool HasValue { get; }
        public T Value { get; }
        public string? MissingReason { get; }

        private OptionalValue(bool hasValue, T value, string? missingReason)
        {
            HasValue = hasValue;
            Value = value;
            MissingReason = missingReason;
        }

        public static OptionalValue<T> Present(T value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value), "OptionalValue.Present requires non-null value.");

            return new OptionalValue<T>(true, value, null);
        }

        public static OptionalValue<T> Missing(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("OptionalValue.Missing requires non-empty reason.", nameof(reason));

            return new OptionalValue<T>(false, default!, reason);
        }

        public T GetValueOrThrow(string? context = null)
        {
            if (!HasValue)
            {
                string prefix = string.IsNullOrWhiteSpace(context) ? "[optional]" : context;
                string reason = MissingReason ?? "<no-reason>";
                throw new InvalidOperationException($"{prefix} Missing value: reason={reason}.");
            }

            return Value;
        }

        public override string ToString()
            => HasValue ? Value?.ToString() ?? "<null>" : $"<missing:{MissingReason}>";
    }
}
