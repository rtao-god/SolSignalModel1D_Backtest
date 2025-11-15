using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Печатает “последний день каждого окна” по схеме блоков: берём takeDays дней, затем пропускаем skipDays.
	/// Для каждого такого окна печатает:
	/// - заголовок блока (№ и даты);
	/// - шапку дня (pred/fact/micro/entry/maxH/minL/close/minMove/причина);
	/// - построчно сделки всех политик в этот день.
	/// </summary>
	public static class WindowTailPrinter
		{
		public static void PrintBlockTails (
			IReadOnlyList<PredictionRecord> records,
			IEnumerable<BacktestPolicyResult> policyResults,
			int takeDays = 20,
			int skipDays = 30,
			string title = "Last day of each window (take → skip)" )
			{
			var recs = (records ?? Array.Empty<PredictionRecord> ()).OrderBy (r => r.DateUtc).ToList ();
			var pol = (policyResults ?? Array.Empty<BacktestPolicyResult> ()).ToList ();
			if (recs.Count == 0 || pol.Count == 0) return;
			if (takeDays <= 0) return;

			ConsoleStyler.WriteHeader ($"=== {title}: {takeDays} → {skipDays} ===");

			int i = 0;
			int blockIdx = 0;

			while (i < recs.Count)
				{
				int start = i;
				int endTake = Math.Min (i + takeDays, recs.Count);  // [start, endTake)
				if (start >= endTake) break;

				var block = recs.GetRange (start, endTake - start);
				var last = block[^1]; // последний день окна = последний из взятых записей

				blockIdx++;
				var blockStartDate = block.First ().DateUtc.Date;
				var blockEndDate = last.DateUtc.Date;

				// Заголовок блока
				ConsoleStyler.WriteHeader ($"--- Блок {blockIdx} [{blockStartDate:yyyy-MM-dd} .. {blockEndDate:yyyy-MM-dd}] — последний день @ {last.DateUtc:yyyy-MM-dd} ---");

				// Шапка дня
				PrintDayHead (last);

				// Сделки всех политик за этот день
				PrintPolicyTradesForDay (last.DateUtc, pol);

				// Шаг к следующему окну: пропускаем skipDays
				i = endTake + skipDays;
				}

			Console.WriteLine ();
			}

		// ===== helpers =====

		private static void PrintDayHead ( PredictionRecord r )
			{
			var t = new TextTable ();
			t.AddHeader ("field", "value");
			t.AddRow ("pred", ClassToStr (r.PredLabel));
			t.AddRow ("micro", r.PredMicroUp ? "UP" : r.PredMicroDown ? "DOWN" : "—");
			t.AddRow ("fact", ClassToStr (r.TrueLabel) + (r.FactMicroUp ? " (micro↑)" : r.FactMicroDown ? " (micro↓)" : ""));
			t.AddRow ("reason", r.Reason);
			t.AddRow ("entry", r.Entry.ToString ("0.0000"));
			t.AddRow ("maxH / minL", $"{r.MaxHigh24:0.0000} / {r.MinLow24:0.0000}");
			t.AddRow ("close24", r.Close24.ToString ("0.0000"));
			t.AddRow ("minMove", (r.MinMove * 100.0).ToString ("0.00") + "%");
			t.WriteToConsole ();
			Console.WriteLine ();
			}

		private static void PrintPolicyTradesForDay ( DateTime dayUtc, IEnumerable<BacktestPolicyResult> policyResults )
			{
			ConsoleStyler.WriteHeader ("Per-policy trades (this day)");
			var t = new TextTable ();
			t.AddHeader ("policy", "source/bucket", "side", "lev", "net %", "entry→exit", "liq?");

			foreach (var pr in policyResults.OrderBy (x => x.PolicyName))
				{
				var dayTrades = pr.Trades?
					.Where (tr => tr.DateUtc.Date == dayUtc.Date)
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
			=> c == 0 ? "Обвал" : (c == 1 ? "Боковик" : "Рост");
		}
	}
