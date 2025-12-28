using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Format;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	public static class SourceBreakdownPrinter
		{
		public static void Print (
			IReadOnlyList<PnLTrade> trades,
			IReadOnlyDictionary<string, int> tradesBySource,
			double startEquity )
			{
			ConsoleStyler.WriteHeader ("=== Breakdown by source ===");

			var t = new TextTable ();
			t.AddHeader ("source", "trades", "PnL%", "comm", "avg Net%");

			// группируем по Source
			var groups = trades
				.GroupBy (tr => tr.Source, StringComparer.OrdinalIgnoreCase)
				.OrderBy (g => g.Key);

			foreach (var g in groups)
				{
				string src = g.Key;

				double investedSum = g.Sum (x => x.PositionUsd);                               // сколько денег заходило
				double netProfit = g.Sum (x => x.PositionUsd * x.NetReturnPct / 100.0);      // уже после комиссий
				double commSum = g.Sum (x => x.Commission);
				double avgNet = g.Count () > 0 ? g.Average (x => x.NetReturnPct) : 0.0;

				double pnlPct = investedSum > 0
					? netProfit / investedSum * 100.0
					: 0.0;

				t.AddRow (
					src,
					g.Count ().ToString (),
					pnlPct.ToString ("0.00"),
					ConsoleNumberFormatter.MoneyShort (commSum),
					avgNet.ToString ("0.000")
				);
				}

			t.WriteToConsole ();
			}
		}
	}
