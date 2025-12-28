using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Format;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	public static class TradeLogPrinter
		{
		public static void Print ( IReadOnlyList<PnLTrade> trades )
			{
			Console.WriteLine ();
			ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
			{
				Console.WriteLine ("=== Trade log (chronological) ===");
			});

			var tLog = new TextTable ();
			tLog.AddHeader (
				"date",
				"entry t",
				"exit t",
				"src",
				"bucket",
				"side",
				"entry px",
				"exit px",
				"gross %",
				"net %",
				"comm",
				"eq",
				"liq");

			foreach (var tr in trades)
				{
				tLog.AddRow (
					tr.DateUtc.ToString ("yyyy-MM-dd"),
					tr.EntryTimeUtc.ToString ("HH:mm"),
					tr.ExitTimeUtc.ToString ("HH:mm"),
					tr.Source,
					tr.Bucket,
					tr.IsLong ? "LONG" : "SHORT",
					ConsoleNumberFormatter.Plain (tr.EntryPrice, 4),
					ConsoleNumberFormatter.Plain (tr.ExitPrice, 4),
					ConsoleNumberFormatter.Plain (tr.GrossReturnPct, 2),
					ConsoleNumberFormatter.Plain (tr.NetReturnPct, 2),
					ConsoleNumberFormatter.MoneyShort (tr.Commission),
					ConsoleNumberFormatter.MoneyShort (tr.EquityAfter),
					tr.IsLiquidated ? "YES" : ""
				);
				}

			tLog.WriteToConsole ();
			}
		}
	}
