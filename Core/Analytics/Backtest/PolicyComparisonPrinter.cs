using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public static class PolicyComparisonPrinter
		{
		public static void PrintSideBySide (
			IReadOnlyList<BacktestPolicyResult> withSl,
			IReadOnlyList<BacktestPolicyResult> noSl )
			{
			Console.WriteLine ();
			ConsoleStyler.WriteHeader ("=== Policies: WITH SL vs NO SL ===");

			var byKeyWith = withSl.ToDictionary (k => (k.PolicyName, k.Margin));
			var byKeyNo = noSl.ToDictionary (k => (k.PolicyName, k.Margin));

			var keys = byKeyWith.Keys
				.Union (byKeyNo.Keys)
				.OrderBy (k => k.PolicyName)
				.ThenBy (k => k.Margin.ToString ())
				.ToList ();

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Trades (SL)",
				"Total% (SL)",
				"MaxDD% (SL)",
				"Trades (noSL)",
				"Total% (noSL)",
				"MaxDD% (noSL)"
			);

			foreach (var key in keys)
				{
				byKeyWith.TryGetValue (key, out var a);
				byKeyNo.TryGetValue (key, out var b);

				var color = (a?.TotalPnlPct ?? 0.0) >= (b?.TotalPnlPct ?? 0.0)
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (color,
					key.PolicyName,
					key.Margin.ToString (),
					(a?.Trades?.Count ?? 0).ToString (),
					(a?.TotalPnlPct ?? 0.0).ToString ("0.00"),
					(a?.MaxDdPct ?? 0.0).ToString ("0.0"),
					(b?.Trades?.Count ?? 0).ToString (),
					(b?.TotalPnlPct ?? 0.0).ToString ("0.00"),
					(b?.MaxDdPct ?? 0.0).ToString ("0.0")
				);
				}

			t.WriteToConsole ();
			}
		}
	}
