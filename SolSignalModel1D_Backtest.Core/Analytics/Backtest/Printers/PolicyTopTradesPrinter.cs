using System;
using System.Linq;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Топ-1 лучшая/худшая сделка для каждой политики, в таблице.
	/// </summary>
	public static class PolicyTopTradesPrinter
		{
		public static void PrintTop1 ( IEnumerable<BacktestPolicyResult> results, string title = "Top 1 best / worst per policy" )
			{
			var list = results?.ToList () ?? new List<BacktestPolicyResult> ();
			if (list.Count == 0) return;

			ConsoleStyler.WriteHeader ($"=== {title} ===");

			var t = new TextTable ();
			t.AddHeader ("policy", "BEST (date/side/pnl/entry→exit/liq)", "WORST (date/side/pnl/entry→exit/liq)");

			foreach (var r in list.OrderBy (x => x.PolicyName))
				{
				if (r.Trades == null || r.Trades.Count == 0)
					{
					t.AddRow (r.PolicyName, "—", "—");
					continue;
					}

				var best = r.Trades.OrderByDescending (tr => tr.NetReturnPct).First ();
				var worst = r.Trades.OrderBy (tr => tr.NetReturnPct).First ();

				string Fmt ( PnLTrade tr )
					{
					string side = tr.IsLong ? "LONG" : "SHORT";
					string liq = tr.IsLiquidated ? "YES" : "no";
					return $"{tr.DateUtc:yyyy-MM-dd}  {side}  {tr.NetReturnPct:+0.00;-0.00}%  entry={tr.EntryPrice:0.0000} exit={tr.ExitPrice:0.0000}  liq={liq}";
					}

				t.AddRow (r.PolicyName, Fmt (best), Fmt (worst));
				}

			t.WriteToConsole ();
			Console.WriteLine ();
			}
		}
	}
