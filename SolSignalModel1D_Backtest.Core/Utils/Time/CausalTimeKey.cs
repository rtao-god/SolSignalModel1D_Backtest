using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Utils.Time
{
    public static class CausalTimeKey
    {
        public static EntryUtc EntryUtc(BacktestRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Causal == null)
                throw new InvalidOperationException("[time] BacktestRecord.Causal is null (invalid record).");

            var t = r.Causal.EntryUtc;
            if (t.Equals(default(EntryUtc)))
                throw new InvalidOperationException("[time] BacktestRecord.Causal.EntryUtc is default (invalid record).");

            return t;
        }

        public static NyTradingEntryUtc EntryUtc(LabeledCausalRow r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Causal == null)
                throw new InvalidOperationException("[time] LabeledCausalRow.Causal is null (invalid row).");

            var t = r.Causal.EntryUtc;
            if (t.Equals(default(NyTradingEntryUtc)))
                throw new InvalidOperationException("[time] LabeledCausalRow.Causal.EntryUtc is default (invalid row).");

            return t;
        }

        public static DayKeyUtc DayKeyUtc(BacktestRecord r) => r.Causal.DayKeyUtc;
        public static DayKeyUtc DayKeyUtc(LabeledCausalRow r) => r.Causal.DayKeyUtc;
    }
}
