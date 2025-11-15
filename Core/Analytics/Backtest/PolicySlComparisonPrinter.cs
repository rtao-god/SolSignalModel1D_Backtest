using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public static class PolicySlComparisonPrinter
		{
		private sealed record Key ( string Policy, string Margin );

		public static void Print (
			IReadOnlyList<BacktestPolicyResult> withSl,
			IReadOnlyList<BacktestPolicyResult> noSl )
			{
			ConsoleStyler.WriteHeader ("=== Policies: WITH SL vs NO SL ===");

			var mapWith = withSl.ToDictionary (
				r => new Key (r.PolicyName, r.Margin.ToString ()),
				r => r);

			var mapNo = noSl.ToDictionary (
				r => new Key (r.PolicyName, r.Margin.ToString ()),
				r => r);

			var keys = mapWith.Keys.Union (mapNo.Keys).OrderBy (k => k.Policy).ThenBy (k => k.Margin);

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Mode",
				"Trades",
				"Total %",
				"Max DD %",
				"Withdrawn",
				"Long n",
				"Short n",
				"Avg Long %",
				"Avg Short %",
				"Liq #"
			);

			foreach (var k in keys)
				{
				if (mapWith.TryGetValue (k, out var w))
					AddRow (t, k, "with SL", w);
				if (mapNo.TryGetValue (k, out var n))
					AddRow (t, k, "without SL", n);
				}

			t.WriteToConsole ();
			}

		private static void AddRow ( TextTable t, Key k, string mode, BacktestPolicyResult r )
			{
			var trades = r.Trades ?? new List<PnLTrade> ();
			var longs = trades.Where (x => x.IsLong).ToList ();
			var shorts = trades.Where (x => !x.IsLong).ToList ();

			double longAvg = longs.Count > 0 ? longs.Average (x => x.NetReturnPct) : 0.0;
			double shortAvg = shorts.Count > 0 ? shorts.Average (x => x.NetReturnPct) : 0.0;
			int liqCnt = trades.Count (x => x.IsLiquidated);

			var color = r.TotalPnlPct >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;

			t.AddColoredRow (color,
				k.Policy,
				k.Margin,
				mode,
				trades.Count.ToString (),
				$"{r.TotalPnlPct:0.00}%",
				$"{r.MaxDdPct:0.0}%",
				$"{r.WithdrawnTotal:0.0}",
				longs.Count.ToString (),
				shorts.Count.ToString (),
				$"{longAvg:0.00}%",
				$"{shortAvg:0.00}%",
				liqCnt.ToString ()
			);
			}
		}
	}
