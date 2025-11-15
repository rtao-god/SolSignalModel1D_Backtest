using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Format;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public static class TopTradesPrinter
		{
		public static void Print ( IReadOnlyList<PnLTrade> trades, double startEquity )
			{
			Console.WriteLine ();
			ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
			{
				Console.WriteLine ("=== Top 10 BEST trades (by Net%) ===");
			});

			var best = trades
				.OrderByDescending (t => t.NetReturnPct)
				.Take (10)
				.ToList ();

			var tBest = new TextTable ();
			tBest.AddHeader ("date", "src", "net %", "comm", "eq after");
			foreach (var b in best)
				{
				tBest.AddRow (
					b.DateUtc.ToString ("yyyy-MM-dd"),
					b.Source,
					ConsoleNumberFormatter.PctShort (b.NetReturnPct),
					ConsoleNumberFormatter.MoneyShort (b.Commission),
					ConsoleNumberFormatter.MoneyShort (b.EquityAfter)
				);
				}
			tBest.WriteToConsole ();

			Console.WriteLine ();
			ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
			{
				Console.WriteLine ("=== Top 10 WORST trades (by Net%) ===");
			});

			var worst = trades
				.OrderBy (t => t.NetReturnPct)
				.Take (10)
				.ToList ();

			var tWorst = new TextTable ();
			tWorst.AddHeader ("date", "src", "net %", "comm", "eq after");
			foreach (var w in worst)
				{
				tWorst.AddRow (
					w.DateUtc.ToString ("yyyy-MM-dd"),
					w.Source,
					ConsoleNumberFormatter.PctShort (w.NetReturnPct),
					ConsoleNumberFormatter.MoneyShort (w.Commission),
					ConsoleNumberFormatter.MoneyShort (w.EquityAfter)
				);
				}
			tWorst.WriteToConsole ();
			}
		}
	}
