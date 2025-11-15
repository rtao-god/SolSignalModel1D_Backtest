using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public static class SlConfusionPrinter
		{
		private const double DailyTpPct = 0.03;
		private const double DailySlPct = 0.03;

		public static void Print ( IReadOnlyList<PredictionRecord> records )
			{
			Console.WriteLine ();
			ConsoleStyler.WriteHeader ("=== Daily TP/SL confusion (surrogate) ===");

			int tpPredHigh = 0;
			int tpPredLow = 0;
			int slPredHigh = 0;
			int slPredLow = 0;

			foreach (var r in records)
				{
				bool goLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool goShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				bool predictedHigh = (r.PredLabel == 0 || r.PredLabel == 2);
				bool isTpDay = false;
				bool isSlDay = false;

				if (goLong)
					{
					double tpPrice = r.Entry * (1.0 + DailyTpPct);
					double slPrice = r.Entry * (1.0 - DailySlPct);
					if (r.MaxHigh24 >= tpPrice) isTpDay = true;
					if (r.MinLow24 <= slPrice) isSlDay = true;
					}
				else
					{
					double tpPrice = r.Entry * (1.0 - DailyTpPct);
					double slPrice = r.Entry * (1.0 + DailySlPct);
					if (r.MinLow24 <= tpPrice) isTpDay = true;
					if (r.MaxHigh24 >= slPrice) isSlDay = true;
					}

				if (isTpDay)
					{
					if (predictedHigh) tpPredHigh++;
					else tpPredLow++;
					}
				else if (isSlDay)
					{
					if (predictedHigh) slPredHigh++;
					else slPredLow++;
					}
				}

			var t = new TextTable ();
			t.AddHeader ("actual \\ predicted", "LOW", "HIGH");
			t.AddRow ("TP-day", tpPredLow.ToString (), tpPredHigh.ToString ());
			t.AddRow ("SL-day", slPredLow.ToString (), slPredHigh.ToString ());
			t.WriteToConsole ();
			}
		}
	}
