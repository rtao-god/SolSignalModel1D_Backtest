using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public static class TopTradesPrinter
		{
		public static void PrintTop1PerPolicy ( IReadOnlyList<PnLTrade> trades )
			{
			Console.WriteLine ();
			ConsoleStyler.WriteHeader ("=== Top 1 best / worst per policy ===");

			var groups = trades
				.GroupBy (t => $"{t.Source}:{t.Bucket}:{t.LeverageUsed:0.0}x")
				.OrderBy (g => g.Key)
				.ToList ();

			var t = new TextTable ();
			t.AddHeader (
				"policy",
				"BEST date", "BEST side", "BEST net%", "BEST entry", "BEST exit", "BEST liq",
				"WORST date", "WORST side", "WORST net%", "WORST entry", "WORST exit", "WORST liq"
			);

			foreach (var g in groups)
				{
				var best = g.OrderByDescending (x => x.NetReturnPct).FirstOrDefault ();
				var worst = g.OrderBy (x => x.NetReturnPct).FirstOrDefault ();

				if (best == null || worst == null)
					continue;

				var color = (best.NetReturnPct - Math.Abs (worst.NetReturnPct)) >= 0
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (color,
					g.Key,
					best.DateUtc.ToString ("yyyy-MM-dd"),
					best.IsLong ? "LONG" : "SHORT",
					best.NetReturnPct.ToString ("+0.00;-0.00"),
					best.EntryPrice.ToString ("0.####"),
					best.ExitPrice.ToString ("0.####"),
					best.IsLiquidated ? "YES" : "no",
					worst.DateUtc.ToString ("yyyy-MM-dd"),
					worst.IsLong ? "LONG" : "SHORT",
					worst.NetReturnPct.ToString ("+0.00;-0.00"),
					worst.EntryPrice.ToString ("0.####"),
					worst.ExitPrice.ToString ("0.####"),
					worst.IsLiquidated ? "YES" : "no"
				);
				}

			t.WriteToConsole ();
			}
		}
	}
