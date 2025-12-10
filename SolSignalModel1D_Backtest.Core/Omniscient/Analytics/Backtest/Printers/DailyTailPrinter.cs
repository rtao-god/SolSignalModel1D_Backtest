using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Печать “хвоста” — последний день диапазона:
	/// - факт/прогноз/микро/причина,
	/// - построчно сделки всех политик в этот день.
	/// Работает поверх BacktestRecord.
	/// </summary>
	public static class DailyTailPrinter
		{
		public static void PrintLastDay (
			IReadOnlyList<BacktestRecord> records,
			IEnumerable<BacktestPolicyResult> policyResults )
			{
			if (records == null || records.Count == 0) return;

			var lastDate = records.Max (r => r.DateUtc);
			var rec = records.First (r => r.DateUtc == lastDate);

			ConsoleStyler.WriteHeader ($"=== LAST DAY @ {lastDate:yyyy-MM-dd} ===");

			var head = new TextTable ();
			head.AddHeader ("field", "value");
			head.AddRow ("pred", ClassToStr (rec.PredLabel));
			head.AddRow ("micro", rec.PredMicroUp ? "UP" : rec.PredMicroDown ? "DOWN" : "—");
			head.AddRow (
				"fact",
				ClassToStr (rec.TrueLabel) +
				(rec.FactMicroUp ? " (micro↑)" : rec.FactMicroDown ? " (micro↓)" : string.Empty));
			head.AddRow ("reason", rec.Reason);
			head.AddRow ("entry", rec.Entry.ToString ("0.0000"));
			head.AddRow ("maxH/minL", $"{rec.MaxHigh24:0.0000} / {rec.MinLow24:0.0000}");
			head.AddRow ("close24", rec.Close24.ToString ("0.0000"));
			head.AddRow ("minMove", (rec.MinMove * 100.0).ToString ("0.00") + "%");
			head.WriteToConsole ();

			Console.WriteLine ();
			ConsoleStyler.WriteHeader ("Per-policy trades (this day)");

			var t = new TextTable ();
			t.AddHeader ("policy", "source/bucket", "side", "lev", "net %", "entry→exit", "liq?");

			foreach (var pr in policyResults.OrderBy (x => x.PolicyName))
				{
				var dayTrades = pr.Trades?
					.Where (tr => tr.DateUtc.Date == lastDate.Date)
					.OrderBy (tr => tr.EntryTimeUtc)
					.ToList () ?? new List<PnLTrade> ();

				if (dayTrades.Count == 0)
					{
					t.AddRow (pr.PolicyName, "—", "—", "—", "—", "—", "—");
					continue;
					}

				foreach (var tr in dayTrades)
					{
					t.AddRow (
						pr.PolicyName,
						$"{tr.Source}/{tr.Bucket}",
						tr.IsLong ? "LONG" : "SHORT",
						$"{tr.LeverageUsed:0.##}x",
						$"{tr.NetReturnPct:+0.00;-0.00}%",
						$"{tr.EntryPrice:0.0000}→{tr.ExitPrice:0.0000}",
						tr.IsLiquidated ? "YES" : "no"
					);
					}
				}

			t.WriteToConsole ();
			Console.WriteLine ();
			}

		private static string ClassToStr ( int c )
			=> c == 0 ? "Обвал" : c == 1 ? "Боковик" : "Рост";
		}
	}
