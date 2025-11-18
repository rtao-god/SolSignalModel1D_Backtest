using System;
using System.Linq;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Микро-слой: 2 блока.
	/// 1) Только по предсказанным БОКОВИКАМ (pred=1): microUp/microDown vs факты.
	///    + цветной summary «слова» с общей точностью.
	/// 2) Для НЕ-боковиков (pred∈{0,2}): обычная направленная точность (без микро-флагов)
	///    + цветной summary по accuracy.
	/// </summary>
	public static class MicroStatsPrinter
		{
		public static void Print ( IReadOnlyList<DataRow> mornings, IReadOnlyList<PredictionRecord> records )
			{
			// mornings пока не используем, оставляем для совместимости сигнатуры
			PrintFlatOnlyMicro (records);
			Console.WriteLine ();
			PrintNonFlatDirection (records);
			}

		// ----- 1) Только по предсказанным БОКОВИКАМ -----
		private static void PrintFlatOnlyMicro ( IReadOnlyList<PredictionRecord> records )
			{
			int microUpPred = 0, microUpHit = 0, microUpMiss = 0;
			int microDownPred = 0, microDownHit = 0, microDownMiss = 0;
			int microNone = 0;

			foreach (var r in records.Where (r => r.PredLabel == 1)) // только flat-предсказания
				{
				bool anyPred = false;

				if (r.PredMicroUp)
					{
					anyPred = true;
					microUpPred++;
					if (r.FactMicroUp) microUpHit++; else microUpMiss++;
					}

				if (r.PredMicroDown)
					{
					anyPred = true;
					microDownPred++;
					if (r.FactMicroDown) microDownHit++; else microDownMiss++;
					}

				if (!anyPred) microNone++;
				}

			ConsoleStyler.WriteHeader ("Micro-layer stats (flat-only)");
			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("pred micro UP (flat)", microUpPred.ToString ());
			t.AddRow ("  └ hit (fact micro UP)", microUpHit.ToString ());
			t.AddRow ("  └ miss", microUpMiss.ToString ());
			t.AddRow ("pred micro DOWN (flat)", microDownPred.ToString ());
			t.AddRow ("  └ hit (fact micro DOWN)", microDownHit.ToString ());
			t.AddRow ("  └ miss", microDownMiss.ToString ());
			t.AddRow ("no micro predicted (flat)", microNone.ToString ());
			t.WriteToConsole ();

			// === Цветной строчный summary ===

			int totalDirPred = microUpPred + microDownPred;
			int totalDirHit = microUpHit + microDownHit;

			if (totalDirPred == 0)
				{
				WriteColoredLine (ConsoleColor.DarkGray,
					"Micro flat-only: нет ни одного micro-сигнала на flat-днях");
				return;
				}

			double accUp = microUpPred > 0
				? (double) microUpHit / microUpPred * 100.0
				: 0.0;

			double accDown = microDownPred > 0
				? (double) microDownHit / microDownPred * 100.0
				: 0.0;

			double accAll = (double) totalDirHit / totalDirPred * 100.0;

			string summary =
				$"Micro flat-only: " +
				$"UP acc = {accUp:0.0}% ({microUpHit}/{microUpPred}), " +
				$"DOWN acc = {accDown:0.0}% ({microDownHit}/{microDownPred}), " +
				$"overall = {accAll:0.0}% ({totalDirHit}/{totalDirPred})";

			var color = accAll >= 50.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
			WriteColoredLine (color, summary);
			}

		// ----- 2) Для НЕ-боковиков: направленная точность -----
		private static void PrintNonFlatDirection ( IReadOnlyList<PredictionRecord> records )
			{
			// Берём только те строки, где предсказание НЕ flat (0 или 2),
			// и сравниваем направление с фактом на 0/2.
			var nonFlat = records.Where (r => r.PredLabel == 0 || r.PredLabel == 2).ToList ();
			int total = nonFlat.Count;
			int correct = nonFlat.Count (r =>
				(r.TrueLabel == 0 && r.PredLabel == 0) ||
				(r.TrueLabel == 2 && r.PredLabel == 2));

			// Разложим по типам для наглядности
			int predUp_factUp = nonFlat.Count (r => r.PredLabel == 2 && r.TrueLabel == 2);
			int predUp_factDown = nonFlat.Count (r => r.PredLabel == 2 && r.TrueLabel == 0);
			int predDown_factDown = nonFlat.Count (r => r.PredLabel == 0 && r.TrueLabel == 0);
			int predDown_factUp = nonFlat.Count (r => r.PredLabel == 0 && r.TrueLabel == 2);

			ConsoleStyler.WriteHeader ("Non-flat direction stats (pred ∈ {down, up})");
			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("pred non-flat total", total.ToString ());
			t.AddRow ("correct (direction)", correct.ToString ());
			t.AddRow ("accuracy", total > 0 ? $"{(double) correct / total * 100.0:0.0}%" : "—");
			t.AddRow ("pred UP & fact UP", predUp_factUp.ToString ());
			t.AddRow ("pred UP & fact DOWN", predUp_factDown.ToString ());
			t.AddRow ("pred DOWN & fact DOWN", predDown_factDown.ToString ());
			t.AddRow ("pred DOWN & fact UP", predDown_factUp.ToString ());
			t.WriteToConsole ();

			// === Цветной строчный summary ===

			if (total == 0)
				{
				WriteColoredLine (ConsoleColor.DarkGray,
					"Non-flat direction: нет ни одного предсказания pred ∈ {down, up}");
				return;
				}

			double acc = (double) correct / total * 100.0;
			string summary = $"Non-flat direction: acc = {acc:0.0}% ({correct}/{total})";

			var colorDir = acc >= 50.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
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
