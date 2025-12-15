using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	public static class DelayedStatsPrinter
		{
		public static void Print ( IReadOnlyList<BacktestRecord> records )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));

			int askedA = 0, usedA = 0, execA = 0, tpA = 0, slA = 0, closeA = 0;
			double sumPctA = 0.0;
			double sumBasePctA = 0.0;

			int askedB = 0, usedB = 0, execB = 0, tpB = 0, slB = 0, closeB = 0;
			double sumPctB = 0.0;
			double sumBasePctB = 0.0;

			foreach (var r in records)
				{
				if (r == null) continue;

				if (r.DelayedSource != "A" && r.DelayedSource != "B")
					continue;

				// Для close@day направление влияет на знак результата.
				// Для TpFirst/SlFirst знак уже зашит в Result + Tp/Sl pct.
				var (wantLong, wantShort, hasDirection) = ResolveDirection (r);

				bool tpFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst;
				bool slFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst;

				if (r.DelayedSource == "A")
					{
					if (r.DelayedEntryAsked == true) askedA++;
					if (r.DelayedEntryUsed == true) usedA++;

					if (r.DelayedEntryExecuted)
						{
						execA++;

						if (tpFirst) tpA++;
						else if (slFirst) slA++;
						else closeA++;

						double delayedPct = CalcUnlevPnlPctOrThrow (r, hasDirection, wantLong, wantShort);
						sumPctA += delayedPct;

						double basePct = CalcBaselineUnlevPnlPctOrThrow (r, hasDirection, wantLong, wantShort);
						sumBasePctA += basePct;
						}
					}
				else
					{
					if (r.DelayedEntryAsked == true) askedB++;
					if (r.DelayedEntryUsed == true) usedB++;

					if (r.DelayedEntryExecuted)
						{
						execB++;

						if (tpFirst) tpB++;
						else if (slFirst) slB++;
						else closeB++;

						double delayedPct = CalcUnlevPnlPctOrThrow (r, hasDirection, wantLong, wantShort);
						sumPctB += delayedPct;

						double basePct = CalcBaselineUnlevPnlPctOrThrow (r, hasDirection, wantLong, wantShort);
						sumBasePctB += basePct;
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
				execA > 0 ? (sumPctA / execA * 100.0).ToString ("0.00") : "—",
				execB > 0 ? (sumPctB / execB * 100.0).ToString ("0.00") : "—",
				execA + execB > 0
					? ((sumPctA + sumPctB) / (execA + execB) * 100.0).ToString ("0.00")
					: "—");

			t.WriteToConsole ();

			PrintBaselineComparison (
				execA, sumPctA, sumBasePctA,
				execB, sumPctB, sumBasePctB);

			PrintSummaryLines (
				execA, tpA, slA, closeA, sumPctA,
				execB, tpB, slB, closeB, sumPctB);
			}

		private static void PrintBaselineComparison (
			int execA, double sumDelayedPctA, double sumBasePctA,
			int execB, double sumDelayedPctB, double sumBasePctB )
			{
			int execTotal = execA + execB;

			double sumDelayedPctTotal = sumDelayedPctA + sumDelayedPctB;
			double sumBasePctTotal = sumBasePctA + sumBasePctB;

			double avgDelayedA = execA > 0 ? sumDelayedPctA / execA * 100.0 : double.NaN;
			double avgBaseA = execA > 0 ? sumBasePctA / execA * 100.0 : double.NaN;
			double deltaAvgA = execA > 0 ? avgDelayedA - avgBaseA : double.NaN;

			double avgDelayedB = execB > 0 ? sumDelayedPctB / execB * 100.0 : double.NaN;
			double avgBaseB = execB > 0 ? sumBasePctB / execB * 100.0 : double.NaN;
			double deltaAvgB = execB > 0 ? avgDelayedB - avgBaseB : double.NaN;

			double avgDelayedTot = execTotal > 0 ? sumDelayedPctTotal / execTotal * 100.0 : double.NaN;
			double avgBaseTot = execTotal > 0 ? sumBasePctTotal / execTotal * 100.0 : double.NaN;
			double deltaAvgTot = execTotal > 0 ? avgDelayedTot - avgBaseTot : double.NaN;

			ConsoleStyler.WriteHeader ("Delayed vs baseline@NY8 (unlevered PnL%)");
			var t = new TextTable ();
			t.AddHeader ("metric", "A", "B", "A+B");

			t.AddRow ("avg delayed PnL% / exec",
				execA > 0 ? $"{avgDelayedA:0.00}%" : "—",
				execB > 0 ? $"{avgDelayedB:0.00}%" : "—",
				execTotal > 0 ? $"{avgDelayedTot:0.00}%" : "—");

			t.AddRow ("avg baseline PnL% / exec",
				execA > 0 ? $"{avgBaseA:0.00}%" : "—",
				execB > 0 ? $"{avgBaseB:0.00}%" : "—",
				execTotal > 0 ? $"{avgBaseTot:0.00}%" : "—");

			t.AddRow ("Δ avg (delayed - base) PnL% / exec",
				execA > 0 ? $"{deltaAvgA:+0.00;-0.00}%" : "—",
				execB > 0 ? $"{deltaAvgB:+0.00;-0.00}%" : "—",
				execTotal > 0 ? $"{deltaAvgTot:+0.00;-0.00}%" : "—");

			t.AddRow ("sum delayed PnL% (no lev)",
				$"{sumDelayedPctA * 100.0:0.00}%",
				$"{sumDelayedPctB * 100.0:0.00}%",
				$"{sumDelayedPctTotal * 100.0:0.00}%");

			t.AddRow ("sum baseline PnL% (no lev)",
				$"{sumBasePctA * 100.0:0.00}%",
				$"{sumBasePctB * 100.0:0.00}%",
				$"{sumBasePctTotal * 100.0:0.00}%");

			t.AddRow ("Δ sum (delayed - base) PnL%",
				$"{(sumDelayedPctA - sumBasePctA) * 100.0:+0.00;-0.00}%",
				$"{(sumDelayedPctB - sumBasePctB) * 100.0:+0.00;-0.00}%",
				$"{(sumDelayedPctTotal - sumBasePctTotal) * 100.0:+0.00;-0.00}%");

			t.WriteToConsole ();

			if (execTotal > 0)
				{
				var colorDelta = deltaAvgTot >= 0.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				string line =
					$"Delayed A+B vs baseline@NY8: Δ avgPnL/exec = {deltaAvgTot:+0.00;-0.00}% " +
					$"(delayed {avgDelayedTot:0.00}%, base {avgBaseTot:0.00}%)";
				WriteColoredLine (colorDelta, line);
				}
			}

		private static void PrintSummaryLines (
			int execA, int tpA, int slA, int closeA, double sumPctA,
			int execB, int tpB, int slB, int closeB, double sumPctB )
			{
			int execTotal = execA + execB;
			int tpTotal = tpA + tpB;
			int slTotal = slA + slB;
			int closeTotal = closeA + closeB;
			double sumPctTotal = sumPctA + sumPctB;

			if (execA == 0)
				{
				WriteColoredLine (ConsoleColor.DarkGray, "Delayed A: нет ни одного исполненного входа");
				}
			else
				{
				double avgA = sumPctA / execA * 100.0;
				double hitRateA = (double) tpA / execA * 100.0;
				double sumA = sumPctA * 100.0;

				var colorA = avgA >= 0.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				string lineA =
					$"Delayed A: exec={execA}, TP={tpA}, SL={slA}, close@day={closeA}, " +
					$"hit-rate={hitRateA:0.0}%, avgPnL/exec={avgA:0.00}%, sumPnL={sumA:0.00}%";
				WriteColoredLine (colorA, lineA);
				}

			if (execB == 0)
				{
				WriteColoredLine (ConsoleColor.DarkGray, "Delayed B: нет ни одного исполненного входа");
				}
			else
				{
				double avgB = sumPctB / execB * 100.0;
				double hitRateB = (double) tpB / execB * 100.0;
				double sumB = sumPctB * 100.0;

				var colorB = avgB >= 0.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				string lineB =
					$"Delayed B: exec={execB}, TP={tpB}, SL={slB}, close@day={closeB}, " +
					$"hit-rate={hitRateB:0.0}%, avgPnL/exec={avgB:0.00}%, sumPnL={sumB:0.00}%";
				WriteColoredLine (colorB, lineB);
				}

			if (execTotal == 0)
				{
				WriteColoredLine (ConsoleColor.DarkGray, "Delayed A+B: нет ни одного исполненного входа");
				}
			else
				{
				double avgTotal = sumPctTotal / execTotal * 100.0;
				double hitRateTotal = (double) tpTotal / execTotal * 100.0;
				double sumTotal = sumPctTotal * 100.0;

				var colorTotal = avgTotal >= 0.0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				string lineTotal =
					$"Delayed A+B: exec={execTotal}, TP={tpTotal}, SL={slTotal}, close@day={closeTotal}, " +
					$"hit-rate={hitRateTotal:0.0}%, avgPnL/exec={avgTotal:0.00}%, sumPnL={sumTotal:0.00}%";
				WriteColoredLine (colorTotal, lineTotal);
				}
			}

		private static double CalcUnlevPnlPctOrThrow ( BacktestRecord r, bool hasDirection, bool wantLong, bool wantShort )
			{
			if (!r.DelayedEntryExecuted)
				throw new InvalidOperationException ($"[delayed] DelayedEntryExecuted=false, но вызван CalcUnlevPnlPctOrThrow для {r.DateUtc:O}.");

			bool tpFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst;
			bool slFirst = r.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst;

			if (tpFirst)
				{
				if (r.DelayedIntradayTpPct is null)
					throw new InvalidOperationException ($"[delayed] TpPct отсутствует при TpFirst для {r.DateUtc:O}.");
				return r.DelayedIntradayTpPct.Value;
				}

			if (slFirst)
				{
				if (r.DelayedIntradaySlPct is null)
					throw new InvalidOperationException ($"[delayed] SlPct отсутствует при SlFirst для {r.DateUtc:O}.");
				return -r.DelayedIntradaySlPct.Value;
				}

			if (r.DelayedEntryPrice <= 0.0)
				throw new InvalidOperationException ($"[delayed] DelayedEntryPrice <= 0 при close@day для {r.DateUtc:O}.");

			if (r.Close24 <= 0.0)
				throw new InvalidOperationException ($"[delayed] Close24 <= 0 при close@day для {r.DateUtc:O}.");

			if (!hasDirection)
				throw new InvalidOperationException ($"[delayed] Нет направления (PredLabel/Micro) для close@day расчёта на {r.DateUtc:O}.");

			if (wantLong) return r.Close24 / r.DelayedEntryPrice - 1.0;
			if (wantShort) return r.DelayedEntryPrice / r.Close24 - 1.0;

			throw new InvalidOperationException ($"[delayed] Неконсистентное направление для {r.DateUtc:O}.");
			}

		private static double CalcBaselineUnlevPnlPctOrThrow ( BacktestRecord r, bool hasDirection, bool wantLong, bool wantShort )
			{
			if (!hasDirection)
				throw new InvalidOperationException ($"[delayed] Нет направления для baseline PnL на {r.DateUtc:O}.");

			if (r.Entry <= 0.0)
				throw new InvalidOperationException ($"[delayed] Entry <= 0 для baseline PnL на {r.DateUtc:O}.");

			if (r.Close24 <= 0.0)
				throw new InvalidOperationException ($"[delayed] Close24 <= 0 для baseline PnL на {r.DateUtc:O}.");

			if (wantLong) return r.Close24 / r.Entry - 1.0;
			if (wantShort) return r.Entry / r.Close24 - 1.0;

			throw new InvalidOperationException ($"[delayed] Неконсистентное направление для baseline на {r.DateUtc:O}.");
			}

		private static (bool wantLong, bool wantShort, bool hasDirection) ResolveDirection ( BacktestRecord r )
			{
			bool wantLong =
				r.PredLabel == 2 ||
				(r.PredLabel == 1 && r.PredMicroUp);

			bool wantShort =
				r.PredLabel == 0 ||
				(r.PredLabel == 1 && r.PredMicroDown);

			return (wantLong, wantShort, wantLong || wantShort);
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
