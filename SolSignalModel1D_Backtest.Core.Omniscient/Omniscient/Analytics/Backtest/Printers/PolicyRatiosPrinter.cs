using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Snapshots.PolicyRatios;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	public static class PolicyRatiosPrinter
		{
		public static void Print (
			IEnumerable<BacktestPolicyResult> results,
			string title = "Policy ratios (Sharpe/Sortino/Calmar)" )
			{
			if (results == null) throw new ArgumentNullException (nameof (results));

			var list = results.ToList ();
			if (list.Count == 0) return;

			// ← вот здесь нужен snapshot-builder из namespace PolicyRatios
			var snapshot = PolicyRatiosSnapshotBuilder.Build (list);

			ConsoleStyler.WriteHeader ($"=== {title} ===");

			var t = new TextTable ();
			t.AddHeader (
				"policy",
				"trades",
				"PnL %",
				"MaxDD %",
				"Sharpe",
				"Sortino",
				"Calmar",
				"WinRate %",
				"Withdrawn $",
				"Liq?");

			foreach (var p in snapshot.Policies.OrderBy (x => x.PolicyName))
				{
				double sharpe = p.Sharpe;
				double sortino = p.Sortino;
				double calmar = p.Calmar;

				t.AddRow (
					p.PolicyName,
					p.TradesCount.ToString (),
					p.TotalPnlPct.ToString ("0.00"),
					p.MaxDdPct.ToString ("0.00"),
					double.IsFinite (sharpe) ? sharpe.ToString ("0.00") : "—",
					double.IsFinite (sortino) ? sortino.ToString ("0.00") : "—",
					double.IsFinite (calmar) ? calmar.ToString ("0.00") : "—",
					(p.WinRate * 100.0).ToString ("0.0"),
					p.WithdrawnTotal.ToString ("0"),
					p.HadLiquidation ? "YES" : "no"
				);
				}

			t.WriteToConsole ();
			Console.WriteLine ();
			}
		}
	}
