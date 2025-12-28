using System;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Printers
	{
	public static class MicroStatsPrinter
		{
		public static void Print ( MicroStatsSnapshot snapshot )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));

			PrintFlatOnlyMicro (snapshot.FlatOnly);
			Console.WriteLine ();

			PrintNonFlatDirection (snapshot.NonFlatDirection);
			}

		private static void PrintFlatOnlyMicro ( FlatOnlyMicroBlock b )
			{
			ConsoleStyler.WriteHeader ("Micro-layer stats (flat-only)");

			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("pred micro UP (flat)", b.MicroUpPred.ToString ());
			t.AddRow ("  └ hit (fact micro UP)", b.MicroUpHit.ToString ());
			t.AddRow ("  └ miss", b.MicroUpMiss.ToString ());
			t.AddRow ("pred micro DOWN (flat)", b.MicroDownPred.ToString ());
			t.AddRow ("  └ hit (fact micro DOWN)", b.MicroDownHit.ToString ());
			t.AddRow ("  └ miss", b.MicroDownMiss.ToString ());
			t.AddRow ("no micro predicted (flat)", b.MicroNonePredicted.ToString ());
			t.WriteToConsole ();

			if (b.TotalDirPred == 0)
				{
				WriteColoredLine (
					ConsoleColor.DarkGray,
					"Micro flat-only: нет ни одного micro-сигнала на flat-днях с валидным micro-фактом");
				return;
				}

			string summary =
				$"Micro flat-only: " +
				$"UP acc = {b.AccUpPct:0.0}% ({b.MicroUpHit}/{b.MicroUpPred}), " +
				$"DOWN acc = {b.AccDownPct:0.0}% ({b.MicroDownHit}/{b.MicroDownPred}), " +
				$"overall = {b.AccAllPct:0.0}% ({b.TotalDirHit}/{b.TotalDirPred})";

			var color = b.AccAllPct >= 50.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
			WriteColoredLine (color, summary);
			}

		private static void PrintNonFlatDirection ( NonFlatDirectionBlock b )
			{
			ConsoleStyler.WriteHeader ("Non-flat direction stats (pred ∈ {down, up})");

			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("pred non-flat total (truth∈{0,2})", b.Total.ToString ());
			t.AddRow ("correct (direction)", b.Correct.ToString ());
			t.AddRow ("accuracy", b.Total > 0 ? $"{b.AccuracyPct:0.0}%" : "—");
			t.AddRow ("pred UP & fact UP", b.PredUp_FactUp.ToString ());
			t.AddRow ("pred UP & fact DOWN", b.PredUp_FactDown.ToString ());
			t.AddRow ("pred DOWN & fact DOWN", b.PredDown_FactDown.ToString ());
			t.AddRow ("pred DOWN & fact UP", b.PredDown_FactUp.ToString ());
			t.WriteToConsole ();

			if (b.Total == 0)
				{
				WriteColoredLine (ConsoleColor.DarkGray,
					"Non-flat direction: нет ни одного случая, где pred∈{down,up} и truth∈{down,up}");
				return;
				}

			string summary = $"Non-flat direction: acc = {b.AccuracyPct:0.0}% ({b.Correct}/{b.Total})";
			var colorDir = b.AccuracyPct >= 50.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
			WriteColoredLine (colorDir, summary);
			}

		private static void WriteColoredLine ( ConsoleColor color, string text )
			{
			var prev = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.WriteLine (text);
			Console.ForegroundColor = prev;
			}
		}
	}
