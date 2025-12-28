using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Format;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	public static class PnlSummaryPrinter
		{
		public static void Print (
			IReadOnlyList<PnLTrade> trades,
			double totalPnlCrossPct,
			double totalPnlIsoPct,
			double maxDdCrossPct,
			double maxDdIsoPct,
			double sharpeAll,
			double sortinoAll,
			double startEquity )
			{
			Console.WriteLine ();
			ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
			{
				Console.WriteLine ("=== PnL summary (ALL trades) ===");
			});

			double sumCommission = trades.Sum (x => x.Commission);
			int totalTrades = trades.Count;
			double finalEquityCross = startEquity * (1.0 + totalPnlCrossPct / 100.0);
			double finalEquityIso = startEquity * (1.0 + totalPnlIsoPct / 100.0);

			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("trades", totalTrades.ToString ());
			t.AddRow ("sum commissions", ConsoleNumberFormatter.MoneyShort (sumCommission));
			t.AddRow ("Total PnL cross", ConsoleNumberFormatter.PctShort (totalPnlCrossPct));
			t.AddRow ("Total PnL cross, x", ConsoleNumberFormatter.Plain (finalEquityCross / startEquity, 3));
			t.AddRow ("Max DD cross", ConsoleNumberFormatter.PctShort (maxDdCrossPct));
			t.AddRow ("Sharpe (daily, combat)", ConsoleNumberFormatter.RatioShort (sharpeAll));
			t.AddRow ("Sortino (daily, combat)", ConsoleNumberFormatter.RatioShort (sortinoAll));
			t.AddRow ("Total PnL isolated", ConsoleNumberFormatter.PctShort (totalPnlIsoPct));
			t.AddRow ("Max DD isolated", ConsoleNumberFormatter.PctShort (maxDdIsoPct));
			t.WriteToConsole ();
			}
		}
	}
