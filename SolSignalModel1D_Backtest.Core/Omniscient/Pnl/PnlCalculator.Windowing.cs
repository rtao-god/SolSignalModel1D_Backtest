using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using System;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
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
			// Канон: baseline end в одном месте и одинаково по всему проекту.
			DateTime expected = Windowing.ComputeBaselineExitUtc (dayStartUtc, nyTz);

			DateTime fromRec = rec.Forward.WindowEndUtc;
			if (fromRec == default)
				return expected;

			if (fromRec.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[pnl] Forward.WindowEndUtc must be UTC, got Kind={fromRec.Kind} at {dayStartUtc:yyyy-MM-dd}.");

			if (fromRec <= dayStartUtc)
				throw new InvalidOperationException ($"[pnl] Forward.WindowEndUtc <= DateUtc at {dayStartUtc:yyyy-MM-dd}: {fromRec:O}.");

			if (fromRec != expected)
				throw new InvalidOperationException (
					$"[pnl] Baseline end mismatch: Forward.WindowEndUtc={fromRec:O}, expected={expected:O} at {dayStartUtc:yyyy-MM-dd}. " +
					"Fix Windowing/RowBuilder to produce canonical window end; do not patch it in PnL.");

			return expected;
			}
		}
	}
