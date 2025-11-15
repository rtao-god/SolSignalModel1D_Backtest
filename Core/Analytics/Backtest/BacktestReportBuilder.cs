using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Format;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Большой финальный отчёт по бэктесту.
	/// Принимает:
	///   - все PredictionRecord (чтобы знать режимы, SL-решения и т.д.)
	///   - результаты по всем политикам плеча и марже
	/// Печатает:
	///   - трейдлог по каждой политике
	///   - summary по каждой политике
	///   - breakdown по source
	///   - топы
	///   - сравнение политик
	///   - policy × regime × margin таблицу
	///   - мини-строку по каждой политике (как ты просил)
	/// </summary>
	public static class BacktestReportBuilder
		{
		private const double StartEquity = 20000.0;

		public static void Print (
			IReadOnlyList<PredictionRecord> allRecords,
			IReadOnlyList<BacktestPolicyResult> policyResults )
			{
			ConsoleStyler.WriteHeader ("==== BACKTEST SUMMARY ====");

			foreach (var res in policyResults)
				{
				ConsoleStyler.WriteHeader ($"== policy: {res.PolicyName} [{res.Margin}] ==");

				// 1) трейдлог наверху
				PrintTradeLog (res.Trades);

				// 2) основной summary
				PrintPolicySummary (allRecords, res);

				// 3) бакеты
				PrintBuckets (res);

				// 4) breakdown по source
				PrintSourceBreakdown (res);

				// 5) топы
				PrintTopTrades (res);

				Console.WriteLine ();
				}

			// общие сравнения
			PrintPolicyComparison (policyResults);
			PrintPolicyRegimeSummary (allRecords, policyResults);
			PrintMiniPolicySummary (policyResults);
			}

		private static void PrintTradeLog ( IReadOnlyList<PnLTrade> trades )
			{
			var t = new TextTable ();
			t.AddHeader ("date", "entry", "exit", "source", "bucket", "side", "entryPx", "exitPx", "gross%", "net%", "comm", "eq");

			foreach (var tr in trades.OrderBy (t => t.DateUtc).ThenBy (t => t.EntryTimeUtc))
				{
				t.AddRow (
					tr.DateUtc.ToString ("yyyy-MM-dd"),
					tr.EntryTimeUtc.ToString ("HH:mm"),
					tr.ExitTimeUtc.ToString ("HH:mm"),
					tr.Source,
					tr.Bucket,
					tr.IsLong ? "LONG" : "SHORT",
					tr.EntryPrice.ToString ("0.####"),
					tr.ExitPrice.ToString ("0.####"),
					tr.GrossReturnPct.ToString ("0.##"),
					tr.NetReturnPct.ToString ("0.##"),
					ConsoleNumberFormatter.MoneyShort (tr.Commission),
					ConsoleNumberFormatter.MoneyShort (tr.EquityAfter) + (tr.IsLiquidated ? " (LIQ)" : "")
				);
				}

			t.WriteToConsole ();
			}

		private static void PrintPolicySummary (
			IReadOnlyList<PredictionRecord> allRecords,
			BacktestPolicyResult res )
			{
			double onExchangeNow = res.BucketSnapshots.Sum (b => b.EquityNow);

			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("trades", res.Trades.Count.ToString ());
			t.AddRow ("on exchange now", ConsoleNumberFormatter.MoneyShort (onExchangeNow));
			t.AddRow ("withdrawn total", ConsoleNumberFormatter.MoneyShort (res.WithdrawnTotal));
			t.AddRow ("Total PnL, %", $"{res.TotalPnlPct:0.00}%");
			t.AddRow ("Max DD, %", $"{res.MaxDdPct:0.0}%");
			t.AddRow ("liquidation", res.HadLiquidation ? "YES" : "no");

			// сколько дней реально спас SL
			int slSavedDays = CountSlSavedDays (allRecords, res.Trades);
			t.AddRow ("SL saved days", slSavedDays.ToString ());

			t.WriteToConsole ();
			}

		private static void PrintBuckets ( BacktestPolicyResult res )
			{
			ConsoleStyler.WriteHeader ("buckets");
			var tb = new TextTable ();
			tb.AddHeader ("bucket", "start", "now", "withdrawn", "total, %");
			foreach (var b in res.BucketSnapshots)
				{
				double total = b.EquityNow + b.Withdrawn;
				double pct = b.StartCapital > 0
					? (total - b.StartCapital) / b.StartCapital * 100.0
					: 0.0;
				tb.AddRow (
					b.Name,
					ConsoleNumberFormatter.MoneyShort (b.StartCapital),
					ConsoleNumberFormatter.MoneyShort (b.EquityNow),
					ConsoleNumberFormatter.MoneyShort (b.Withdrawn),
					pct.ToString ("0.0")
				);
				}
			tb.WriteToConsole ();
			}

		private static void PrintSourceBreakdown ( BacktestPolicyResult res )
			{
			ConsoleStyler.WriteHeader ("Breakdown by source");
			var t = new TextTable ();
			t.AddHeader ("source", "trades", "pnl, %", "comm", "avg net%");

			var bySrc = res.Trades
				.GroupBy (tr => tr.Source)
				.OrderBy (g => g.Key);

			foreach (var g in bySrc)
				{
				int trades = g.Count ();
				double sumNet = g.Sum (x => x.NetReturnPct);
				double avgNet = trades > 0 ? sumNet / trades : 0.0;
				double comm = g.Sum (x => x.Commission);

				// pnl, % считаем как сумму net% по сделкам этого источника
				t.AddRow (
					g.Key,
					trades.ToString (),
					sumNet.ToString ("0.0"),
					ConsoleNumberFormatter.MoneyShort (comm),
					avgNet.ToString ("0.00")
				);
				}

			t.WriteToConsole ();
			}

		private static void PrintTopTrades ( BacktestPolicyResult res )
			{
			ConsoleStyler.WriteHeader ("Top 5 BEST trades (by Net%)");
			var best = res.Trades
				.OrderByDescending (tr => tr.NetReturnPct)
				.Take (5)
				.ToList ();

			var tBest = new TextTable ();
			tBest.AddHeader ("date", "source", "net%", "pos, $");
			foreach (var tr in best)
				{
				tBest.AddRow (
					tr.DateUtc.ToString ("yyyy-MM-dd"),
					tr.Source,
					tr.NetReturnPct.ToString ("0.##"),
					ConsoleNumberFormatter.MoneyShort (tr.PositionUsd)
				);
				}
			tBest.WriteToConsole ();

			ConsoleStyler.WriteHeader ("Top 5 WORST trades (by Net%)");
			var worst = res.Trades
				.OrderBy (tr => tr.NetReturnPct)
				.Take (5)
				.ToList ();

			var tWorst = new TextTable ();
			tWorst.AddHeader ("date", "source", "net%", "pos, $");
			foreach (var tr in worst)
				{
				tWorst.AddRow (
					tr.DateUtc.ToString ("yyyy-MM-dd"),
					tr.Source,
					tr.NetReturnPct.ToString ("0.##"),
					ConsoleNumberFormatter.MoneyShort (tr.PositionUsd)
				);
				}
			tWorst.WriteToConsole ();
			}

		private static void PrintPolicyComparison ( IReadOnlyList<BacktestPolicyResult> results )
			{
			if (results.Count == 0) return;

			ConsoleStyler.WriteHeader ("== policy × margin comparison ==");

			var best = results.OrderByDescending (r => r.TotalPnlPct).First ();
			var worst = results.OrderBy (r => r.TotalPnlPct).First ();

			var t = new TextTable ();
			t.AddHeader ("policy", "margin", "PnL %", "Max DD %", "withdrawn", "liq");

			foreach (var r in results.OrderByDescending (r => r.TotalPnlPct))
				{
				var row = new[]
				{
					r.PolicyName,
					r.Margin.ToString(),
					$"{r.TotalPnlPct:0.00}%",
					$"{r.MaxDdPct:0.0}%",
					ConsoleNumberFormatter.MoneyShort(r.WithdrawnTotal),
					r.HadLiquidation ? "YES" : "-"
				};

				if (r == best)
					{
					ConsoleStyler.WithColor (ConsoleStyler.GoodColor, () => t.AddRow (row));
					}
				else if (r == worst)
					{
					ConsoleStyler.WithColor (ConsoleStyler.BadColor, () => t.AddRow (row));
					}
				else
					{
					t.AddRow (row);
					}
				}

			t.WriteToConsole ();
			}

		private static void PrintPolicyRegimeSummary (
			IReadOnlyList<PredictionRecord> allRecords,
			IReadOnlyList<BacktestPolicyResult> results )
			{
			var rows = BacktestSummaryTableBuilder.Build (allRecords, results);
			if (rows.Count == 0) return;

			ConsoleStyler.WriteHeader ("== policy × regime × margin (avg returns) ==");

			var t = new TextTable ();
			t.AddHeader ("policy", "margin", "regime", "days", "trades", "pnl, $", "avg/day", "avg/week", "avg/month", "micro", "SL");
			foreach (var r in rows)
				{
				t.AddRow (
					r.Policy,
					r.Margin.ToString (),
					r.Regime,
					r.Days.ToString (),
					r.Trades.ToString (),
					ConsoleNumberFormatter.MoneyShort (r.TotalPnlUsd),
					ConsoleNumberFormatter.MoneyShort (r.AvgDayUsd),
					ConsoleNumberFormatter.MoneyShort (r.AvgWeekUsd),
					ConsoleNumberFormatter.MoneyShort (r.AvgMonthUsd),
					r.HasMicro ? "yes" : "no",
					r.HasSl ? "yes" : "no"
				);
				}

			t.WriteToConsole ();
			}

		private static void PrintMiniPolicySummary ( IReadOnlyList<BacktestPolicyResult> results )
			{
			ConsoleStyler.WriteHeader ("== mini policy summary ==");

			var t = new TextTable ();
			t.AddHeader ("policy", "first trade", "trades", "Total PnL %", "liq", "best", "worst");

			foreach (var r in results)
				{
				var first = r.Trades.OrderBy (x => x.DateUtc).FirstOrDefault ();
				var best = r.Trades.OrderByDescending (x => x.NetReturnPct).FirstOrDefault ();
				var worst = r.Trades.OrderBy (x => x.NetReturnPct).FirstOrDefault ();

				string firstStr = first != null
					? $"{first.DateUtc:yyyy-MM-dd} {first.Source} {first.Bucket} {(first.IsLong ? "LONG" : "SHORT")} {first.EntryPrice:0.####}->{first.ExitPrice:0.####}"
					: "-";

				string bestStr = best != null
					? $"{best.DateUtc:yyyy-MM-dd} {best.Source} {best.NetReturnPct:0.##}%"
					: "-";

				string worstStr = worst != null
					? $"{worst.DateUtc:yyyy-MM-dd} {worst.Source} {worst.NetReturnPct:0.##}%"
					: "-";

				t.AddRow (
					$"{r.PolicyName} [{r.Margin}]",
					firstStr,
					r.Trades.Count.ToString (),
					$"{r.TotalPnlPct:0.00}%",
					r.HadLiquidation ? "YES" : "no",
					bestStr,
					worstStr
				);
				}

			t.WriteToConsole ();
			}

		private static int CountSlSavedDays (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<PnLTrade> trades )
			{
			var tradedDates = new HashSet<DateTime> (trades.Select (t => t.DateUtc.Date));
			int saved = 0;

			foreach (var r in records)
				{
				bool goLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool goShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				bool isSlDay = false;
				if (goLong)
					{
					if (r.MinLow24 > 0 && r.Entry > 0)
						isSlDay = r.MinLow24 <= r.Entry * (1.0 - 0.05);
					}
				else
					{
					if (r.MaxHigh24 > 0 && r.Entry > 0)
						isSlDay = r.MaxHigh24 >= r.Entry * (1.0 + 0.05);
					}

				if (!isSlDay)
					continue;

				if (r.SlHighDecision && !tradedDates.Contains (r.DateUtc.Date))
					saved++;
				}

			return saved;
			}

		public static void PrintPolicies (
			IReadOnlyList<PredictionRecord> allRecords,
			IReadOnlyList<BacktestPolicyResult> policyResults )
			{
			Print (allRecords, policyResults);
			}
		}
	}
