using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	public static partial class PnlCalculator
		{
		private static DateTime RequireUtcDayStart ( DateTime dt, string name )
			{
			if (dt.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[pnl] {name} must be UTC: {dt:O} (Kind={dt.Kind}).");

			if (dt.Hour != 0 || dt.Minute != 0 || dt.Second != 0 || dt.Millisecond != 0)
				throw new InvalidOperationException ($"[pnl] {name} must be a UTC day start: {dt:O}.");

			return dt;
			}

		private static DateTime GetBaselineWindowEndUtcOrFail ( BacktestRecord rec, DateTime dayStartUtc, TimeZoneInfo nyTz )
			{
            if (rec == null) throw new ArgumentNullException(nameof(rec));
            if (rec.Causal == null)
                throw new InvalidOperationException($"[pnl] rec.Causal is null at {dayStartUtc:yyyy-MM-dd} — causal layer missing.");
            if (rec.Forward == null)
                throw new InvalidOperationException($"[pnl] rec.Forward is null at {dayStartUtc:yyyy-MM-dd} — forward layer missing.");

            // baseline end в одном месте и одинаково по всему проекту.
            var entryUtcDt = rec.Causal.EntryUtc.Value;
            if (entryUtcDt.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException(
                    $"[pnl] Causal.EntryUtc must be UTC at {dayStartUtc:yyyy-MM-dd}: {entryUtcDt:O} (Kind={entryUtcDt.Kind}).");

            DateTime expected = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtcDt), nyTz).Value;

            DateTime fromRec = rec.Forward.WindowEndUtc;
            if (fromRec == default)
                throw new InvalidOperationException(
                    $"[pnl] Forward.WindowEndUtc is default at {dayStartUtc:yyyy-MM-dd}. Forward facts must be initialized.");

            if (fromRec.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[pnl] Forward.WindowEndUtc must be UTC, got Kind={fromRec.Kind} at {dayStartUtc:yyyy-MM-dd}.");

            if (fromRec <= entryUtcDt)
                throw new InvalidOperationException(
                    $"[pnl] Forward.WindowEndUtc <= entryUtc at {dayStartUtc:yyyy-MM-dd}: entryUtc={entryUtcDt:O}, windowEnd={fromRec:O}.");

            return expected;
			}
		}
	}

