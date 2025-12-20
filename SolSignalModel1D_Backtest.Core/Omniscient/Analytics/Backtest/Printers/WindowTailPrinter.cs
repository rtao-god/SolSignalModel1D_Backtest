using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Печатает “последний день каждого окна” по схеме блоков: берём takeDays дней, затем пропускаем skipDays.
	/// </summary>
	public static class WindowTailPrinter
		{
		public static void PrintBlockTails (
			IReadOnlyList<LabeledCausalRow> mornings,
			IReadOnlyList<BacktestRecord> records,
			IEnumerable<BacktestPolicyResult> policyResults,
			int takeDays = 20,
			int skipDays = 30,
			string title = "Last day of each window (take → skip)" )
			{
			var recs = (records ?? Array.Empty<BacktestRecord> ()).OrderBy (r => r.DateUtc).ToList ();
			var pol = (policyResults ?? Array.Empty<BacktestPolicyResult> ()).ToList ();
			if (recs.Count == 0 || pol.Count == 0) return;
			if (takeDays <= 0) return;
			if (mornings == null || mornings.Count == 0) return;

			// Быстрая мапа: дата → BacktestRecord (а не truth-строка).
			var byDate = recs.ToDictionary (r => r.ToCausalDateUtc (), r => r);

			ConsoleStyler.WriteHeader ($"=== {title}: {takeDays} → {skipDays} ===");

			int i = 0;
			int blockIdx = 0;

			while (i < recs.Count)
				{
				int start = i;
				int endTake = Math.Min (i + takeDays, recs.Count);  // [start, endTake)
				if (start >= endTake) break;

				var block = recs.GetRange (start, endTake - start);
				var lastRec = block[^1];

				blockIdx++;
				var blockStartDate = block.First ().DateUtc.ToCausalDateUtc ();
				var blockEndDate = lastRec.DateUtc.ToCausalDateUtc ();

				byDate.TryGetValue (lastRec.ToCausalDateUtc (), out var dayRec);

				ConsoleStyler.WriteHeader (
					$"--- Блок {blockIdx} [{blockStartDate:yyyy-MM-dd} .. {blockEndDate:yyyy-MM-dd}] — последний день @ {lastRec.DateUtc:yyyy-MM-dd} ---");

				PrintDayHead (dayRec, lastRec);
				PrintPolicyTradesForDay (lastRec.DateUtc, pol);

				i = endTake + skipDays;
				}

			Console.WriteLine ();
			}

		private static void PrintDayHead ( BacktestRecord? row, BacktestRecord r )
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

			if (row != null)
				{
				t.AddRow ("path dir", PathDirToStr (row.Forward.PathFirstPassDir));
				t.AddRow (
					"path firstPass",
					row.Forward.PathFirstPassTimeUtc.HasValue
						? row.Forward.PathFirstPassTimeUtc.Value.ToString ("yyyy-MM-dd HH:mm")
						: "—"
				);
				t.AddRow (
					"path up% / down%",
					$"{row.Forward.PathReachedUpPct * 100.0:0.00}% / {row.Forward.PathReachedDownPct * 100.0:0.00}%"
				);
				}

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
					.Where (tr => tr.DateUtc.ToCausalDateUtc () == dayUtc.ToCausalDateUtc ())
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

		private static string ClassToStr ( int c ) => c switch
			{
				0 => "0 (down)",
				1 => "1 (flat)",
				2 => "2 (up)",
				_ => c.ToString ()
				};

		private static string PathDirToStr ( int dir ) => dir switch
			{
				> 0 => "UP (first)",
				< 0 => "DOWN (first)",
				_ => "FLAT / none"
				};
		}
	}
