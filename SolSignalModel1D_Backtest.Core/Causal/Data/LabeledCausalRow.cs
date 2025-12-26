using System;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
{
    public sealed class LabeledCausalRow
    {
        public CausalDataRow Causal { get; }
        public int TrueLabel { get; }
        public bool FactMicroUp { get; }
        public bool FactMicroDown { get; }

        public NyTradingEntryUtc EntryUtc => Causal.EntryUtc;
        public EntryUtc RawEntryUtc => Causal.RawEntryUtc;

        public EntryDayKeyUtc EntryDayKeyUtc => Causal.EntryDayKeyUtc;

        [Obsolete("Use EntryDayKeyUtc (explicit entry day-key).", error: false)]
        public EntryDayKeyUtc DayKeyUtc => EntryDayKeyUtc;

        public LabeledCausalRow(CausalDataRow causal, int trueLabel, bool factMicroUp, bool factMicroDown)
        {
            Causal = causal ?? throw new ArgumentNullException(nameof(causal));

            if (trueLabel < 0 || trueLabel > 2)
                throw new ArgumentOutOfRangeException(nameof(trueLabel), trueLabel, "TrueLabel must be in [0..2].");

            if (factMicroUp && factMicroDown)
                throw new InvalidOperationException("[LabeledCausalRow] FactMicroUp and FactMicroDown cannot be true одновременно.");

            TrueLabel = trueLabel;
            FactMicroUp = factMicroUp;
            FactMicroDown = factMicroDown;
        }
    }
}
