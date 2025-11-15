using System;
using System.Linq;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Печать «модельных» статистик
	/// - дневная путаница (per-class acc + overall)
	/// - SL-модель (runtime)
	/// Микро и Delayed печатаются отдельными принтерами и здесь отсутствуют.
	/// </summary>
	public static class BacktestModelStatsPrinter
		{
		private const double DailySlPct = 0.05;

		public static void Print ( IReadOnlyList<PredictionRecord> records )
			{
			ConsoleStyler.WriteHeader ("==== MODEL STATS ====");

			PrintDailyConfusion (records);
			Console.WriteLine ();

			PrintSlConfusion (records);
			Console.WriteLine ();
			}

		// ===== 1) Дневная путаница =====
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
			t.AddHeader ("true ↓ / pred →", "0", "1", "2", "correct / total (acc)");
			for (int y = 0; y < 3; y++)
				{
				int correct = m[y, y];
				int totalRow = rowSum[y];
				double acc = totalRow > 0 ? (double) correct / totalRow * 100.0 : 0.0;

				t.AddRow (
					LabelName (y),
					m[y, 0].ToString (),
					m[y, 1].ToString (),
					m[y, 2].ToString (),
					$"{correct}/{totalRow} ({acc:0.0}%)"
				);
				}

			int diag = m[0, 0] + m[1, 1] + m[2, 2];
			double accuracy = total > 0 ? (double) diag / total * 100.0 : 0.0;
			t.AddRow ("Accuracy (overall)", "", "", "", $"{diag}/{total} ({accuracy:0.0}%)");
			t.WriteToConsole ();
			}

		private static string LabelName ( int x ) => x switch
			{
				0 => "0 (down)",
				1 => "1 (flat)",
				2 => "2 (up)",
				_ => x.ToString ()
				};

		// ===== 2) SL-модель (runtime) =====
		private static void PrintSlConfusion ( IReadOnlyList<PredictionRecord> records )
			{
			int tp_low = 0, tp_high = 0, sl_low = 0, sl_high = 0;
			int slSaved = 0;

			foreach (var r in records)
				{
				bool goLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool goShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);
				if (!goLong && !goShort) continue;

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

				bool predHigh = r.SlHighDecision;

				if (!isSlDay)
					{
					if (predHigh) tp_high++; else tp_low++;
					}
				else
					{
					if (predHigh) sl_high++; else sl_low++;
					}

				if (isSlDay && predHigh) slSaved++;
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
