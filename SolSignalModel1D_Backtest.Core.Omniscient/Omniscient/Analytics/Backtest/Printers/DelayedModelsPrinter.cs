using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	public static class DelayedModelsPrinter
		{
		public static void Print ( IReadOnlyList<BacktestRecord> records )
			{
			Console.WriteLine ();
			ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
			{
				Console.WriteLine ("=== Delayed models (A/B) ===");
			});

			var delayedA = records.Where (r => r.DelayedSource == "A").ToList ();
			var delayedB = records.Where (r => r.DelayedSource == "B").ToList ();

			var t = new TextTable ();
			t.AddHeader ("model", "asked", "executed", "tp", "sl", "avg improv");

			AddRow (t, "DelayedA", delayedA);
			AddRow (t, "DelayedB", delayedB);

			t.WriteToConsole ();
			}

		private static void AddRow ( TextTable t, string name, List<BacktestRecord> list )
			{
			int asked = list.Count;

			int executed = list.Count (r => r.DelayedExecution is not null);

			int tp = list.Count (r => r.DelayedExecution?.IntradayResult == DelayedIntradayResult.TpFirst);
			int sl = list.Count (r => r.DelayedExecution?.IntradayResult == DelayedIntradayResult.SlFirst);

			double avgImprov = 0.0;
			int improvCnt = 0;

			foreach (var r in list)
				{
				var exec = r.DelayedExecution;
				if (exec is null)
					continue;

				bool goLong = r.PredLabel == 2 || r.PredLabel == 1 && r.PredMicroUp;
				bool goShort = r.PredLabel == 0 || r.PredLabel == 1 && r.PredMicroDown;
				if (!goLong && !goShort)
					continue;

				double baseEntry = r.Entry;
				double delayedEntry = exec.EntryPrice;

				double improv = goLong
					? (baseEntry - delayedEntry) / baseEntry
					: (delayedEntry - baseEntry) / baseEntry;

				avgImprov += improv;
				improvCnt++;
				}

			if (improvCnt > 0)
				avgImprov /= improvCnt;

			t.AddRow (
				name,
				asked.ToString (),
				executed.ToString (),
				tp.ToString (),
				sl.ToString (),
				(avgImprov * 100.0).ToString ("0.000") + "%"
			);
			}
		}
	}
