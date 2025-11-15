using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Единый отчёт по Delayed A/B:
	/// - asked / used / executed / TP-first / SL-first / close@day (по A/B и итого)
	/// - суммарный PnL% (без плеча) и средний PnL% на исполнение
	/// </summary>
	public static class DelayedStatsPrinter
		{
		public static void Print ( IReadOnlyList<PredictionRecord> records )
			{
			int askedA = 0, usedA = 0, execA = 0, tpA = 0, slA = 0, closeA = 0; double sumPctA = 0.0;
			int askedB = 0, usedB = 0, execB = 0, tpB = 0, slB = 0, closeB = 0; double sumPctB = 0.0;

			foreach (var r in records)
				{
				if (r.DelayedSource != "A" && r.DelayedSource != "B") continue;
				bool wantLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool wantShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);

				if (r.DelayedSource == "A")
					{
					if (r.DelayedEntryAsked) askedA++;
					if (r.DelayedEntryUsed) usedA++;

					if (r.DelayedEntryExecuted)
						{
						execA++;
						bool tpFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst;
						bool slFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst;

						if (tpFirst) tpA++;
						else if (slFirst) slA++;
						else closeA++;

						sumPctA += CalcUnlevPnlPct (r, wantLong, wantShort);
						}
					}
				else // "B"
					{
					if (r.DelayedEntryAsked) askedB++;
					if (r.DelayedEntryUsed) usedB++;

					if (r.DelayedEntryExecuted)
						{
						execB++;
						bool tpFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst;
						bool slFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst;

						if (tpFirst) tpB++;
						else if (slFirst) slB++;
						else closeB++;

						sumPctB += CalcUnlevPnlPct (r, wantLong, wantShort);
						}
					}
				}

			ConsoleStyler.WriteHeader ("Delayed A/B stats (counts & unlevered PnL%)");
			var t = new TextTable ();
			t.AddHeader ("metric", "A", "B", "Total");

			t.AddRow ("asked", askedA.ToString (), askedB.ToString (), (askedA + askedB).ToString ());
			t.AddRow ("used", usedA.ToString (), usedB.ToString (), (usedA + usedB).ToString ());
			t.AddRow ("executed", execA.ToString (), execB.ToString (), (execA + execB).ToString ());
			t.AddRow ("TP-first", tpA.ToString (), tpB.ToString (), (tpA + tpB).ToString ());
			t.AddRow ("SL-first", slA.ToString (), slB.ToString (), (slA + slB).ToString ());
			t.AddRow ("close@day", closeA.ToString (), closeB.ToString (), (closeA + closeB).ToString ());

			t.AddRow ("sum PnL % (no lev)",
				(sumPctA * 100.0).ToString ("0.00"),
				(sumPctB * 100.0).ToString ("0.00"),
				((sumPctA + sumPctB) * 100.0).ToString ("0.00"));

			t.AddRow ("avg PnL % / exec",
				execA > 0 ? ((sumPctA / execA) * 100.0).ToString ("0.00") : "—",
				execB > 0 ? ((sumPctB / execB) * 100.0).ToString ("0.00") : "—",
				(execA + execB) > 0 ? (((sumPctA + sumPctB) / (execA + execB)) * 100.0).ToString ("0.00") : "—");

			t.WriteToConsole ();
			}

		private static double CalcUnlevPnlPct ( PredictionRecord r, bool wantLong, bool wantShort )
			{
			bool tpFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst;
			bool slFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst;

			if (tpFirst) return r.DelayedIntradayTpPct;
			if (slFirst) return -r.DelayedIntradaySlPct;

			if (!r.DelayedEntryExecuted || r.DelayedEntryPrice <= 0 || r.Close24 <= 0) return 0.0;

			if (wantLong) return (r.Close24 / r.DelayedEntryPrice) - 1.0;
			if (wantShort) return (r.DelayedEntryPrice / r.Close24) - 1.0;
			return 0.0;
			}
		}
	}
