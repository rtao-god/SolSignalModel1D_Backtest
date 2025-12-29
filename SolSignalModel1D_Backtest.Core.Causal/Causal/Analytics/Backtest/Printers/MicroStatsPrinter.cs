using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
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
			t.AddRow ("fact micro days (flat)", b.TotalFactDays.ToString ());
			t.AddRow ("pred micro UP (flat)", b.MicroUpPred.ToString ());
			t.AddRow ("  └ hit (fact micro UP)", b.MicroUpHit.ToString ());
			t.AddRow ("  └ miss", b.MicroUpMiss.ToString ());
			t.AddRow ("pred micro DOWN (flat)", b.MicroDownPred.ToString ());
			t.AddRow ("  └ hit (fact micro DOWN)", b.MicroDownHit.ToString ());
			t.AddRow ("  └ miss", b.MicroDownMiss.ToString ());
			t.AddRow ("no micro predicted (flat)", b.MicroNonePredicted.ToString ());
			t.AddRow ("micro coverage (pred / fact)",
				FormatOptionalPct (b.CoveragePct, $"{b.TotalDirPred}/{b.TotalFactDays}"));
			t.WriteToConsole ();

			if (b.TotalDirPred == 0)
				{
				WriteColoredLine (
					ConsoleColor.DarkGray,
					"Micro flat-only: нет ни одного micro-сигнала на днях с валидным micro-фактом");
				return;
				}

			string summary =
				$"Micro flat-only: " +
				$"UP acc = {FormatOptionalPct (b.AccUpPct, $"{b.MicroUpHit}/{b.MicroUpPred}")}, " +
				$"DOWN acc = {FormatOptionalPct (b.AccDownPct, $"{b.MicroDownHit}/{b.MicroDownPred}")}, " +
				$"overall(pred) = {FormatOptionalPct (b.AccAllPct, $"{b.TotalDirHit}/{b.TotalDirPred}")}, " +
				$"overall(all) = {FormatOptionalPct (b.AccAllWithNonePct, $"{b.TotalDirHit}/{b.TotalFactDays}")}";

			var color = b.AccAllWithNonePct.HasValue && b.AccAllWithNonePct.Value >= 50.0
				? ConsoleStyler.GoodColor
				: ConsoleStyler.BadColor;
			WriteColoredLine (color, summary);
			}

		private static void PrintNonFlatDirection ( NonFlatDirectionBlock b )
			{
			ConsoleStyler.WriteHeader ("Non-flat direction stats (pred ∈ {down, up})");

			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("pred non-flat total (truth∈{0,2})", b.Total.ToString ());
			t.AddRow ("correct (direction)", b.Correct.ToString ());
			t.AddRow ("accuracy", FormatOptionalPct (b.AccuracyPct));
			t.AddRow ("pred UP & fact UP", b.PredUp_FactUp.ToString ());
			t.AddRow ("pred UP & fact DOWN", b.PredUp_FactDown.ToString ());
			t.AddRow ("pred DOWN & fact DOWN", b.PredDown_FactDown.ToString ());
			t.AddRow ("pred DOWN & fact UP", b.PredDown_FactUp.ToString ());
			t.WriteToConsole ();

			if (!b.AccuracyPct.HasValue)
				{
				WriteColoredLine (ConsoleColor.DarkGray,
					$"Non-flat direction: {b.AccuracyPct.MissingReason}");
				return;
				}

			string summary = $"Non-flat direction: acc = {b.AccuracyPct.Value:0.0}% ({b.Correct}/{b.Total})";
			var colorDir = b.AccuracyPct.Value >= 50.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
			WriteColoredLine (colorDir, summary);
			}

		private static string FormatOptionalPct ( OptionalValue<double> value, string? suffix = null )
			{
			if (value.HasValue)
				{
				string extra = string.IsNullOrWhiteSpace (suffix) ? "" : $" ({suffix})";
				return $"{value.Value:0.0}%{extra}";
				}

			string reason = string.IsNullOrWhiteSpace (value.MissingReason) ? "Missing" : value.MissingReason;
			return $"— ({reason})";
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
