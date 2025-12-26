using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
{
    public sealed class ForwardOutcomes
    {
        // Invariant: entry timestamp is always UTC and is the start of the baseline window for this record.
        public EntryUtc EntryUtc { get; init; }

        // Stable day identity (UTC 00:00).
        public DayKeyUtc DayKeyUtc
        {
            get
            {
                if (EntryUtc.IsDefault)
                    throw new InvalidOperationException("[forward] EntryUtc is default (uninitialized).");

                return EntryUtc.DayKeyUtc;
            }
        }

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
