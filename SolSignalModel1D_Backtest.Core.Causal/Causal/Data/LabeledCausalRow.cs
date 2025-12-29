using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
{
    public sealed class LabeledCausalRow
    {
        public CausalDataRow Causal { get; }
        public int TrueLabel { get; }
        public OptionalValue<MicroTruthDirection> MicroTruth { get; }

        public EntryUtc EntryUtc => Causal.EntryUtc;
        public NyTradingEntryUtc TradingEntryUtc => Causal.TradingEntryUtc;

        public EntryDayKeyUtc EntryDayKeyUtc => Causal.EntryDayKeyUtc;


        public LabeledCausalRow(
            CausalDataRow causal,
            int trueLabel,
            OptionalValue<MicroTruthDirection> microTruth)
        {
            Causal = causal ?? throw new ArgumentNullException(nameof(causal));

            if (trueLabel < 0 || trueLabel > 2)
                throw new ArgumentOutOfRangeException(nameof(trueLabel), trueLabel, "TrueLabel must be in [0..2].");

            if (microTruth.HasValue && trueLabel != 1)
            {
                throw new InvalidOperationException(
                    $"[LabeledCausalRow] MicroTruth must be missing for non-flat label. trueLabel={trueLabel}.");
            }

            TrueLabel = trueLabel;
            MicroTruth = microTruth;
        }

        public bool TryGetMicroTruth(out MicroTruthDirection direction)
        {
            if (!MicroTruth.HasValue)
            {
                direction = default;
                return false;
            }

            direction = MicroTruth.Value;
            return true;
        }

        public MicroTruthDirection GetMicroTruthOrThrow(string? context = null)
            => MicroTruth.GetValueOrThrow(context ?? "[LabeledCausalRow] MicroTruth missing.");
    }
}
