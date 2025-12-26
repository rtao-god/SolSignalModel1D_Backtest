using System;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers
	{
	public static class AggregationProbsPrinter
		{
		public static void Print ( AggregationProbsSnapshot snapshot )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));

			ConsoleStyler.WriteHeader ("==== AGGREGATION PROBS ====");

			if (snapshot.TotalInputRecords == 0)
				{
				Console.WriteLine ("[agg-probs] no records, nothing to print.");
				return;
				}

			Console.WriteLine (
				$"[agg-probs] full input period = {snapshot.MinDateUtc:yyyy-MM-dd}..{snapshot.MaxDateUtc:yyyy-MM-dd}, totalRecords = {snapshot.TotalInputRecords}");

			if (snapshot.ExcludedCount > 0)
				{
				Console.WriteLine ($"[agg-probs][WARN] excluded days (no baseline-exit) = {snapshot.ExcludedCount}");
				}

			var meta = new TextTable ();
			meta.AddHeader ("segment", "from", "to", "days");

			foreach (var seg in snapshot.Segments)
				{
				if (seg.RecordsCount == 0)
					{
					meta.AddRow (seg.SegmentLabel, "-", "-", "0");
					continue;
					}

				meta.AddRow (
					seg.SegmentLabel,
					seg.FromDateUtc!.Value.ToString ("yyyy-MM-dd"),
					seg.ToDateUtc!.Value.ToString ("yyyy-MM-dd"),
					seg.RecordsCount.ToString ());
				}

			meta.WriteToConsole ();
			Console.WriteLine ();

			foreach (var seg in snapshot.Segments)
				PrintSegment (seg);

			PrintLastDaysDebug (snapshot);
			}

		private static void PrintSegment ( AggregationProbsSegmentSnapshot seg )
			{
			ConsoleStyler.WriteHeader ($"[agg-probs] {seg.SegmentName}");

			if (seg.RecordsCount == 0)
				{
				Console.WriteLine ("[agg-probs] segment is empty.");
				Console.WriteLine ();
				return;
				}

			var probsTable = new TextTable ();
			probsTable.AddHeader ("layer", "P_up", "P_flat", "P_down", "sum");

			probsTable.AddRow ("Day",
				FormatProb (seg.Day.PUp), FormatProb (seg.Day.PFlat), FormatProb (seg.Day.PDown), FormatProb (seg.Day.Sum));

			probsTable.AddRow ("Day+Micro",
				FormatProb (seg.DayMicro.PUp), FormatProb (seg.DayMicro.PFlat), FormatProb (seg.DayMicro.PDown), FormatProb (seg.DayMicro.Sum));

			probsTable.AddRow ("Total (Day+Micro+SL)",
				FormatProb (seg.Total.PUp), FormatProb (seg.Total.PFlat), FormatProb (seg.Total.PDown), FormatProb (seg.Total.Sum));

			probsTable.WriteToConsole ();
			Console.WriteLine ();

			var confTable = new TextTable ();
			confTable.AddHeader ("metric", "value");
			confTable.AddRow ("Conf_Day (avg)", FormatProb (seg.AvgConfDay));
			confTable.AddRow ("Conf_Micro (avg)", FormatProb (seg.AvgConfMicro));
			confTable.AddRow ("records with SL-score", $"{seg.RecordsWithSlScore}/{seg.RecordsCount}");
			confTable.WriteToConsole ();
			Console.WriteLine ();
			}

		private static void PrintLastDaysDebug ( AggregationProbsSnapshot snapshot )
			{
			if (snapshot.DebugLastDays == null || snapshot.DebugLastDays.Count == 0)
				return;

			ConsoleStyler.WriteHeader ($"[agg-probs] last {snapshot.DebugLastDays.Count} days (debug)");

			var t = new TextTable ();
			t.AddHeader (
				"Date",
				"y",
				"predD",
				"predDM",
				"predTot",
				"P_d (u/f/d)",
				"P_dm (u/f/d)",
				"P_tot (u/f/d)",
				"microUsed",
				"slUsed",
				"microAgree",
				"slPenLong",
				"slPenShort");

			foreach (var r in snapshot.DebugLastDays)
				{
				string pDay = $"{FormatProb (r.PDay.Up)}/{FormatProb (r.PDay.Flat)}/{FormatProb (r.PDay.Down)}";
				string pDm = $"{FormatProb (r.PDayMicro.Up)}/{FormatProb (r.PDayMicro.Flat)}/{FormatProb (r.PDayMicro.Down)}";
				string pTot = $"{FormatProb (r.PTotal.Up)}/{FormatProb (r.PTotal.Flat)}/{FormatProb (r.PTotal.Down)}";

				t.AddRow (
					r.DateUtc.ToString ("yyyy-MM-dd"),
					r.TrueLabel.ToString (),
					r.PredDay.ToString (),
					r.PredDayMicro.ToString (),
					r.PredTotal.ToString (),
					pDay,
					pDm,
					pTot,
					r.MicroUsed ? "Y" : ".",
					r.SlUsed ? "Y" : ".",
					r.MicroAgree ? "Y" : ".",
					r.SlPenLong ? "Y" : ".",
					r.SlPenShort ? "Y" : ".");
				}

			t.WriteToConsole ();
			Console.WriteLine ();
			}

		private static string FormatProb ( double x ) => x.ToString ("0.000");
		}
	}
