using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time
{
    public static class CausalTimeKey
    {
        public static EntryUtc EntryUtc(BacktestRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Forward == null)
                throw new InvalidOperationException("[time] BacktestRecord.Forward is null (invalid record).");

            var t = r.Forward.EntryUtc;
            if (t.IsDefault)
                throw new InvalidOperationException("[time] BacktestRecord.Forward.EntryUtc is default (invalid record).");

            return t;
        }

        public static NyTradingEntryUtc TradingEntryUtc(LabeledCausalRow r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Causal == null)
                throw new InvalidOperationException("[time] LabeledCausalRow.Causal is null (invalid row).");

            var t = r.Causal.TradingEntryUtc;
            if (t.Equals(default(NyTradingEntryUtc)))
                throw new InvalidOperationException("[time] LabeledCausalRow.Causal.TradingEntryUtc is default (invalid row).");

            return t;
        }

        /// <summary>Day identity записи (entry-day-key, 00:00Z).</summary>
        public static EntryDayKeyUtc EntryDayKeyUtc(BacktestRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return r.EntryDayKeyUtc;
        }

        /// <summary>Day identity записи (entry-day-key, 00:00Z).</summary>
        public static EntryDayKeyUtc EntryDayKeyUtc(LabeledCausalRow r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return r.EntryDayKeyUtc;
        }
    }
}
