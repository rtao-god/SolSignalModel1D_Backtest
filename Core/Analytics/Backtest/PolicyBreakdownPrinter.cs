using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public static class PolicyBreakdownPrinter
		{
		public static void PrintSummary ( IReadOnlyList<BacktestPolicyResult> results )
			=> PrintSummary (results, "Policy summary");

		public static void PrintSummary ( IReadOnlyList<BacktestPolicyResult> results, string title )
			{
			ConsoleStyler.WriteHeader (title);

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Trades",
				"W/L",
				"WinRate",
				"Avg trade %",
				"Long cnt",
				"Short cnt",
				"Long $PnL",
				"Short $PnL",
				"Total $PnL",
				"Withdrawn $",
				"Total %",
				"Max DD %",
				"Liq #"
			);

			foreach (var r in results.OrderBy (x => x.PolicyName).ThenBy (x => x.Margin.ToString ()))
				{
				var trades = r.Trades ?? new List<PnLTrade> ();
				var longs = trades.Where (x => x.IsLong).ToList ();
				var shorts = trades.Where (x => !x.IsLong).ToList ();

				int w = trades.Count (x => x.NetReturnPct > 0.0);
				int l = trades.Count - w;
				string wl = $"{w}/{l}";
				string winRate = trades.Count > 0 ? $"{(double) w / trades.Count * 100.0:0.0}%" : "—";
				double avgTradePct = trades.Count > 0 ? trades.Average (x => x.NetReturnPct) : 0.0;

				double longUsd = longs.Sum (x => x.PositionUsd * (x.NetReturnPct / 100.0));
				double shortUsd = shorts.Sum (x => x.PositionUsd * (x.NetReturnPct / 100.0));
				double totalUsd = longUsd + shortUsd;

				int liqCnt = trades.Count (x => x.IsLiquidated);

				var line = new[]
				{
					r.PolicyName,
					r.Margin.ToString(),
					trades.Count.ToString(),
					wl,
					winRate,
					$"{avgTradePct:0.00}%",
					longs.Count.ToString(),
					shorts.Count.ToString(),
					$"{Math.Round(longUsd,2):0.##}$",
					$"{Math.Round(shortUsd,2):0.##}$",
					$"{Math.Round(totalUsd,2):0.##}$",
					$"{Math.Round(r.WithdrawnTotal,2):0.##}$",
					$"{r.TotalPnlPct:0.00}%",
					$"{r.MaxDdPct:0.00}%",
					liqCnt.ToString()
				};

				var color = r.TotalPnlPct >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				t.AddColoredRow (color, line);
				}

			t.WriteToConsole ();
			}

		/// <summary>
		/// Месячный «скос» направления (агрегировано по всем политикам).
		/// </summary>
		public static void PrintMonthlySkew ( IReadOnlyList<BacktestPolicyResult> results, int months = 12 )
			{
			var allTrades = results.SelectMany (r => r.Trades ?? Enumerable.Empty<PnLTrade> ()).ToList ();
			if (allTrades.Count == 0) return;

			var groups = allTrades
				.GroupBy (t => new { Y = t.DateUtc.Year, M = t.DateUtc.Month })
				.OrderBy (g => g.Key.Y).ThenBy (g => g.Key.M)
				.ToList ();

			if (groups.Count == 0) return;

			var last = groups.Skip (Math.Max (0, groups.Count - months)).ToList ();

			var t = new TextTable ();
			t.AddHeader ("Month", "Long cnt", "Short cnt", "Long avg %", "Short avg %");

			foreach (var g in last)
				{
				var longs = g.Where (x => x.IsLong).ToList ();
				var shorts = g.Where (x => !x.IsLong).ToList ();

				double longAvg = longs.Count > 0 ? longs.Average (x => x.NetReturnPct) : 0.0;
				double shortAvg = shorts.Count > 0 ? shorts.Average (x => x.NetReturnPct) : 0.0;

				var monthStr = $"{g.Key.Y}-{g.Key.M:00}";
				var color = (longAvg + shortAvg) >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;

				t.AddColoredRow (color,
					monthStr,
					longs.Count.ToString (),
					shorts.Count.ToString (),
					$"{longAvg:0.00}%",
					$"{shortAvg:0.00}%"
				);
				}

			t.WriteToConsole ();
			}
		}
	}
