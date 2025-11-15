using System;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics
	{
	public sealed class ExecutionStats
		{
		// SL слой
		public int SlScored;
		public double SlProbSum;
		public int SlProbCount;
		public int SlSaidRisk;
		public int SlFactSlFirst;
		public int SlFactTpFirst;

		// немедленные входы (когда SL сказал "норм")
		public int ImmediateTrades;
		public int ImmediateTp;
		public int ImmediateSl;
		public int ImmediateAmbiguous;
		public int ImmediateNone;

		// delayed (все)
		public int DelayedAskedAll;
		public int DelayedExecutedAll;
		public int DelayedTpAll;
		public int DelayedSlAll;
		public int DelayedAmbAll;
		public int DelayedNoneAll;
		public double DelayedEqAll = 1.0;
		public double DelayedPeakAll = 1.0;
		public double DelayedMaxDdAll = 0.0;

		// delayed A
		public int DelayedAskedA;
		public int DelayedExecutedA;
		public int DelayedTpA;
		public int DelayedSlA;
		public int DelayedAmbA;
		public int DelayedNoneA;
		public double DelayedEqA = 1.0;
		public double DelayedPeakA = 1.0;
		public double DelayedMaxDdA = 0.0;

		// delayed B
		public int DelayedAskedB;
		public int DelayedExecutedB;
		public int DelayedTpB;
		public int DelayedSlB;
		public int DelayedAmbB;
		public int DelayedNoneB;
		public double DelayedEqB = 1.0;
		public double DelayedPeakB = 1.0;
		public double DelayedMaxDdB = 0.0;

		public void AddSlScore ( double prob, bool saidRisk, HourlyTradeOutcome baseOutcome )
			{
			SlScored++;
			SlProbSum += prob;
			SlProbCount++;
			if (saidRisk) SlSaidRisk++;

			if (baseOutcome.Result == HourlyTradeResult.SlFirst)
				SlFactSlFirst++;
			else if (baseOutcome.Result == HourlyTradeResult.TpFirst)
				SlFactTpFirst++;
			}

		public void AddImmediate ( HourlyTradeOutcome outcome )
			{
			ImmediateTrades++;
			switch (outcome.Result)
				{
				case HourlyTradeResult.TpFirst:
					ImmediateTp++;
					break;
				case HourlyTradeResult.SlFirst:
					ImmediateSl++;
					break;
				case HourlyTradeResult.Ambiguous:
					ImmediateAmbiguous++;
					break;
				default:
					ImmediateNone++;
					break;
				}
			}

		public void AddDelayed ( string source, DelayedEntryResult res )
			{
			// all
			DelayedAskedAll++;
			if (res.Executed)
				{
				DelayedExecutedAll++;
				double ret = 0.0;
				if (res.Result == DelayedIntradayResult.TpFirst)
					{
					DelayedTpAll++;
					ret = res.TpPct;
					}
				else if (res.Result == DelayedIntradayResult.SlFirst)
					{
					DelayedSlAll++;
					ret = -res.SlPct;
					}
				else if (res.Result == DelayedIntradayResult.Ambiguous)
					{
					DelayedAmbAll++;
					}
				else
					{
					DelayedNoneAll++;
					}

				if (ret != 0.0)
					{
					DelayedEqAll *= (1.0 + ret);
					if (DelayedEqAll > DelayedPeakAll) DelayedPeakAll = DelayedEqAll;
					double dd = (DelayedPeakAll - DelayedEqAll) / DelayedPeakAll;
					if (dd > DelayedMaxDdAll) DelayedMaxDdAll = dd;
					}
				}
			else
				{
				DelayedNoneAll++;
				}

			// per-source
			if (source == "A")
				{
				DelayedAskedA++;
				if (res.Executed)
					{
					DelayedExecutedA++;
					double ret = 0.0;
					if (res.Result == DelayedIntradayResult.TpFirst)
						{
						DelayedTpA++;
						ret = res.TpPct;
						}
					else if (res.Result == DelayedIntradayResult.SlFirst)
						{
						DelayedSlA++;
						ret = -res.SlPct;
						}
					else if (res.Result == DelayedIntradayResult.Ambiguous)
						{
						DelayedAmbA++;
						}
					else
						{
						DelayedNoneA++;
						}

					if (ret != 0.0)
						{
						DelayedEqA *= (1.0 + ret);
						if (DelayedEqA > DelayedPeakA) DelayedPeakA = DelayedEqA;
						double dd = (DelayedPeakA - DelayedEqA) / DelayedPeakA;
						if (dd > DelayedMaxDdA) DelayedMaxDdA = dd;
						}
					}
				else
					{
					DelayedNoneA++;
					}
				}
			else if (source == "B")
				{
				DelayedAskedB++;
				if (res.Executed)
					{
					DelayedExecutedB++;
					double ret = 0.0;
					if (res.Result == DelayedIntradayResult.TpFirst)
						{
						DelayedTpB++;
						ret = res.TpPct;
						}
					else if (res.Result == DelayedIntradayResult.SlFirst)
						{
						DelayedSlB++;
						ret = -res.SlPct;
						}
					else if (res.Result == DelayedIntradayResult.Ambiguous)
						{
						DelayedAmbB++;
						}
					else
						{
						DelayedNoneB++;
						}

					if (ret != 0.0)
						{
						DelayedEqB *= (1.0 + ret);
						if (DelayedEqB > DelayedPeakB) DelayedPeakB = DelayedEqB;
						double dd = (DelayedPeakB - DelayedEqB) / DelayedPeakB;
						if (dd > DelayedMaxDdB) DelayedMaxDdB = dd;
						}
					}
				else
					{
					DelayedNoneB++;
					}
				}
			}

		public void Print ()
			{
			// delayed (all)
			ConsoleStyler.WriteHeader ("=== Delayed (all) ===");
			var tAll = new TextTable ();
			tAll.AddHeader ("metric", "value");
			tAll.AddRow ("asked", DelayedAskedAll.ToString ());
			tAll.AddRow ("executed", DelayedExecutedAll.ToString ());
			tAll.AddRow ("tp", DelayedTpAll.ToString ());
			tAll.AddRow ("sl", DelayedSlAll.ToString ());
			tAll.AddRow ("ambiguous", DelayedAmbAll.ToString ());
			tAll.AddRow ("none", DelayedNoneAll.ToString ());
			tAll.AddRow ("Total PnL, %", ((DelayedEqAll - 1.0) * 100.0).ToString ("0.0"));
			tAll.AddRow ("Total PnL, x", DelayedEqAll.ToString ("0.00"));
			tAll.AddRow ("Max DD, %", (DelayedMaxDdAll * 100.0).ToString ("0.0"));
			tAll.WriteToConsole ();

			// A
			ConsoleStyler.WriteHeader ("=== Delayed from A ===");
			var tA = new TextTable ();
			tA.AddHeader ("metric", "value");
			tA.AddRow ("asked", DelayedAskedA.ToString ());
			tA.AddRow ("executed", DelayedExecutedA.ToString ());
			tA.AddRow ("tp", DelayedTpA.ToString ());
			tA.AddRow ("sl", DelayedSlA.ToString ());
			tA.AddRow ("ambiguous", DelayedAmbA.ToString ());
			tA.AddRow ("none", DelayedNoneA.ToString ());
			tA.AddRow ("Total PnL, %", ((DelayedEqA - 1.0) * 100.0).ToString ("0.0"));
			tA.AddRow ("Total PnL, x", DelayedEqA.ToString ("0.00"));
			tA.AddRow ("Max DD, %", (DelayedMaxDdA * 100.0).ToString ("0.0"));
			tA.WriteToConsole ();

			// B
			ConsoleStyler.WriteHeader ("=== Delayed from B ===");
			var tB = new TextTable ();
			tB.AddHeader ("metric", "value");
			tB.AddRow ("asked", DelayedAskedB.ToString ());
			tB.AddRow ("executed", DelayedExecutedB.ToString ());
			tB.AddRow ("tp", DelayedTpB.ToString ());
			tB.AddRow ("sl", DelayedSlB.ToString ());
			tB.AddRow ("ambiguous", DelayedAmbB.ToString ());
			tB.AddRow ("none", DelayedNoneB.ToString ());
			tB.AddRow ("Total PnL, %", ((DelayedEqB - 1.0) * 100.0).ToString ("0.0"));
			tB.AddRow ("Total PnL, x", DelayedEqB.ToString ("0.00"));
			tB.AddRow ("Max DD, %", (DelayedMaxDdB * 100.0).ToString ("0.0"));
			tB.WriteToConsole ();

			ConsoleStyler.WriteHeader ("=== Execution stats ===");
			var tE = new TextTable ();
			tE.AddHeader ("metric", "value");
			tE.AddRow ("SL scored", SlScored.ToString ());
			tE.AddRow ("SL avg prob", SlProbCount > 0 ? (SlProbSum / SlProbCount).ToString ("0.000") : "0.000");
			tE.AddRow ("SL said risk", SlSaidRisk.ToString ());
			tE.AddRow ("SL fact SL-first (12:00)", SlFactSlFirst.ToString ());
			tE.AddRow ("SL fact TP-first (12:00)", SlFactTpFirst.ToString ());
			tE.AddRow ("immediate trades", ImmediateTrades.ToString ());
			tE.AddRow ("immediate TP", ImmediateTp.ToString ());
			tE.AddRow ("immediate SL", ImmediateSl.ToString ());
			tE.AddRow ("immediate ambiguous", ImmediateAmbiguous.ToString ());
			tE.AddRow ("immediate none", ImmediateNone.ToString ());
			tE.WriteToConsole ();
			}
		}
	}
