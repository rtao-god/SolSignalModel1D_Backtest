using SolSignalModel1D_Backtest.Core.Omniscient.Utils;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	public static class PolicyAveragesPrinter
		{
		public static void PrintHospitalAverages ( IReadOnlyList<BacktestPolicyResult> results )
			{
			Console.WriteLine ();
			ConsoleStyler.WriteHeader ("=== Policy averages (USD): day / week / month ===");

			var t = new TextTable ();
			t.AddHeader ("Policy", "Margin", "Days", "Avg/day $", "Avg/week $", "Avg/month $");

			foreach (var r in results
						 .OrderBy (x => x.PolicyName)
						 .ThenBy (x => x.Margin.ToString ()))
				{
				// агрегируем по дням (USD)
				var byDay = r.Trades
					.GroupBy (tr => tr.DateUtc.ToCausalDateUtc())
					.Select (g => g.Sum (tr => tr.PositionUsd * tr.NetReturnPct / 100.0))
					.ToList ();

				int days = byDay.Count;
				double avgDay = days > 0 ? byDay.Average () : 0.0;

				var color = avgDay >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;

				t.AddColoredRow (color,
					r.PolicyName,
					r.Margin.ToString (),
					days.ToString (),
					avgDay.ToString ("0.00"),
					(avgDay * 7.0).ToString ("0.00"),
					(avgDay * 30.0).ToString ("0.00")
				);
				}

			t.WriteToConsole ();
			}
		}
	}
