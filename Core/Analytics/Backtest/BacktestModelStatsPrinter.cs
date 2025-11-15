using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Format;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Печать всех "модельных" статистик: дневная путаница, микро, delayed, SL.
	/// Ждёт, что PredictionRecord содержит:
	///  - SlHighDecision (runtime)
	///  - DelayedSource / DelayedEntryExecuted / DelayedIntradayResult / DelayedIntradayTpPct / DelayedIntradaySlPct
	/// </summary>
	public static class BacktestModelStatsPrinter
		{
		private const double DailySlPct = 0.05;

		public static void Print ( IReadOnlyList<PredictionRecord> records )
			{
			ConsoleStyler.WriteHeader ("==== MODEL STATS ====");

			PrintDailyConfusion (records);
			Console.WriteLine ();

			PrintMicroStats (records);
			Console.WriteLine ();

			PrintDelayedStats (records);
			Console.WriteLine ();

			PrintSlConfusion (records);
			Console.WriteLine ();
			}

		// ===== дневная =====
		private static void PrintDailyConfusion ( IReadOnlyList<PredictionRecord> records )
			{
			int[,] m = new int[3, 3];
			int[] rowSum = new int[3];
			int total = 0;

			foreach (var r in records)
				{
				if (r.TrueLabel is < 0 or > 2) continue;
				if (r.PredLabel is < 0 or > 2) continue;

				m[r.TrueLabel, r.PredLabel]++;
				rowSum[r.TrueLabel]++;
				total++;
				}

			ConsoleStyler.WriteHeader ("Daily label confusion");
			var t = new TextTable ();
			t.AddHeader ("true ↓ / pred →", "0", "1", "2", "row %");

			for (int y = 0; y < 3; y++)
				{
				double correct = m[y, y];
				double rowPct = rowSum[y] > 0 ? correct / rowSum[y] * 100.0 : 0.0;
				t.AddRow (
					LabelName (y),
					m[y, 0].ToString (),
					m[y, 1].ToString (),
					m[y, 2].ToString (),
					rowPct.ToString ("0.0") + "%"
				);
				}

			double diag = m[0, 0] + m[1, 1] + m[2, 2];
			double globalAcc = total > 0 ? diag / total * 100.0 : 0.0;
			t.AddRow ("global acc", "", "", "", globalAcc.ToString ("0.0") + "%");

			t.WriteToConsole ();
			}

		private static string LabelName ( int x )
			{
			return x switch
				{
					0 => "0 (down)",
					1 => "1 (flat)",
					2 => "2 (up)",
					_ => x.ToString ()
					};
			}

		// ===== микро =====
		private static void PrintMicroStats ( IReadOnlyList<PredictionRecord> records )
			{
			int microUpPred = 0;
			int microUpHit = 0;
			int microUpMiss = 0;

			int microDownPred = 0;
			int microDownHit = 0;
			int microDownMiss = 0;

			int microNone = 0;

			foreach (var r in records)
				{
				bool anyPred = false;

				if (r.PredMicroUp)
					{
					anyPred = true;
					microUpPred++;
					if (r.FactMicroUp) microUpHit++;
					else microUpMiss++;
					}

				if (r.PredMicroDown)
					{
					anyPred = true;
					microDownPred++;
					if (r.FactMicroDown) microDownHit++;
					else microDownMiss++;
					}

				if (!anyPred)
					microNone++;
				}

			ConsoleStyler.WriteHeader ("Micro-layer stats");
			var t = new TextTable ();
			t.AddHeader ("metric", "value");
			t.AddRow ("pred micro UP (total)", microUpPred.ToString ());
			t.AddRow ("  └ hit (fact micro UP)", microUpHit.ToString ());
			t.AddRow ("  └ miss", microUpMiss.ToString ());
			t.AddRow ("pred micro DOWN (total)", microDownPred.ToString ());
			t.AddRow ("  └ hit (fact micro DOWN)", microDownHit.ToString ());
			t.AddRow ("  └ miss", microDownMiss.ToString ());
			t.AddRow ("no micro predicted", microNone.ToString ());
			t.WriteToConsole ();

			if (microDownPred == 0)
				{
				ConsoleStyler.WithColor (ConsoleStyler.BadColor, () =>
				{
					Console.WriteLine ("[micro] warning: PredMicroDown ни разу не сработал. Проверь заполнение PredictionRecord.PredMicroDown / FactMicroDown.");
				});
				}
			}

		// ===== delayed =====
		private static void PrintDelayedStats ( IReadOnlyList<PredictionRecord> records )
			{
			int askedA = 0, execA = 0, tpA = 0, slA = 0;
			int askedB = 0, execB = 0, tpB = 0, slB = 0;

			foreach (var r in records)
				{
				if (r.DelayedSource == "A")
					{
					askedA++;
					if (r.DelayedEntryExecuted) execA++;
					if (r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst) tpA++;
					if (r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst) slA++;
					}
				else if (r.DelayedSource == "B")
					{
					askedB++;
					if (r.DelayedEntryExecuted) execB++;
					if (r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst) tpB++;
					if (r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst) slB++;
					}
				}

			ConsoleStyler.WriteHeader ("Delayed A/B stats");
			var t = new TextTable ();
			t.AddHeader ("metric", "A", "B");
			t.AddRow ("asked", askedA.ToString (), askedB.ToString ());
			t.AddRow ("executed", execA.ToString (), execB.ToString ());
			t.AddRow ("tp", tpA.ToString (), tpB.ToString ());
			t.AddRow ("sl", slA.ToString (), slB.ToString ());
			t.WriteToConsole ();
			}

		// ===== SL =====
		private static void PrintSlConfusion ( IReadOnlyList<PredictionRecord> records )
			{
			int tp_low = 0, tp_high = 0, sl_low = 0, sl_high = 0;
			int slSaved = 0;

			// дни, когда мы фактически торговали, чтобы понять "спас или нет"
			var tradedDates = new HashSet<DateTime> (records.Select (r => r.DateUtc.Date)); // если хочешь — сюда можно кидать из трейдлога

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
						isSlDay = r.MinLow24 <= r.Entry * (1.0 - DailySlPct);
					}
				else
					{
					if (r.MaxHigh24 > 0 && r.Entry > 0)
						isSlDay = r.MaxHigh24 >= r.Entry * (1.0 + DailySlPct);
					}

				bool predHigh = r.SlHighDecision; // вот тут главное отличие

				if (!isSlDay)
					{
					if (predHigh) tp_high++;
					else tp_low++;
					}
				else
					{
					if (predHigh) sl_high++;
					else sl_low++;
					}

				// если день был SL-днём и SL сказал HIGH — считаем как "мог спасти"
				if (isSlDay && predHigh)
					slSaved++;
				}

			ConsoleStyler.WriteHeader ("SL-model confusion (runtime)");
			var t = new TextTable ();
			t.AddHeader ("day type", "pred LOW", "pred HIGH");
			t.AddRow ("TP-day", tp_low.ToString (), tp_high.ToString ());
			t.AddRow ("SL-day", sl_low.ToString (), sl_high.ToString ());
			t.AddRow ("SL saved (potential)", slSaved.ToString (), "");
			t.WriteToConsole ();
			}
		}
	}
