using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
{
    public sealed class ForwardOutcomes
    {
        // Invariant: entry timestamp is always UTC and is the start of the baseline window for this record.
        public required EntryUtc EntryUtc { get; init; }

        // Stable day identity for this record: entry day-key (UTC 00:00).
        public EntryDayKeyUtc EntryDayKeyUtc
        {
            get
            {
                if (EntryUtc.IsDefault)
                    throw new InvalidOperationException("[forward] EntryUtc is default (uninitialized).");

                return SolSignalModel1D_Backtest.Core.Time.EntryDayKeyUtc.FromUtcMomentOrThrow(EntryUtc.Value);
            }
        }

        // Back-compat (temporary): prefer EntryDayKeyUtc.
        [Obsolete("Use EntryDayKeyUtc (explicit entry day-key).", error: false)]
        public EntryDayKeyUtc DayKeyUtc => EntryDayKeyUtc;

        public DateTime WindowEndUtc { get; init; }

        public double Entry { get; init; }
        public double MaxHigh24 { get; init; }
        public double MinLow24 { get; init; }
        public double Close24 { get; init; }

        public IReadOnlyList<Candle1m> DayMinutes { get; init; } = Array.Empty<Candle1m>();

        public double MinMove { get; init; }

        public int TrueLabel { get; init; }
        public bool FactMicroUp { get; init; }
        public bool FactMicroDown { get; init; }

        public int PathFirstPassDir { get; init; }
        public DateTime? PathFirstPassTimeUtc { get; init; }
        public double PathReachedUpPct { get; init; }
        public double PathReachedDownPct { get; init; }
    }
}
