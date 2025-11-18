// File: Core/Analytics/Backtest/BacktestModelStatsPrinter.cs
using System;
using System.Linq;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Печать «модельных» статистик:
	/// - дневная путаница (per-class acc + overall)
	/// - путаница по тренду (UP vs DOWN, с учётом micro в предсказании)
	/// - SL-модель (runtime, path-based по 1m, с теми же TP/SL%, что и в PnL)
	///   + цветной summary по основным метрикам SL.
	/// Все расчёты SL-исхода привязаны к окну [t0; t_exit), где
	/// t0 = PredictionRecord.DateUtc, t_exit = Windowing.ComputeBaselineExitUtc(t0, nyTz).
	/// </summary>
	public static class BacktestModelStatsPrinter
		{
		/// <summary>
		/// Основная точка входа:
		/// - Daily confusion по классам (0/1/2);
		/// - Trend-confusion по направлению (DOWN vs UP);
		/// - SL-model confusion + метрики (TPR/FPR/Precision/Recall/F1/PR-AUC, coverage).
		/// </summary>
		public static void Print (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			double dailyTpPct,
			double dailySlPct,
			TimeZoneInfo nyTz )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));

			ConsoleStyler.WriteHeader ("==== MODEL STATS ====");

			// 1) Обычная 3-классовая путаница
			PrintDailyConfusion (records);
			Console.WriteLine ();

			// 2) Путаница по тренду (UP vs DOWN)
			PrintTrendDirectionConfusion (records);
			Console.WriteLine ();

			// 3) SL-модель (path-based по 1m) в том же окне, что и таргеты/PnL
			PrintSlConfusionPathBased (records, sol1m, dailyTpPct, dailySlPct, nyTz);
			Console.WriteLine ();
			}

		// ===== 1) Дневная путаница (3 класса) =====

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

			ConsoleStyler.WriteHeader ("Daily label confusion (3-class)");
			var t = new TextTable ();
			t.AddHeader ("true label", "pred 0", "pred 1", "pred 2", "correct", "total", "acc %");

			// «Бросок монеты» для 3 классов — 1/3 ≈ 33.3%
			double baseline = 100.0 / 3.0;

			int diag = 0;

			for (int y = 0; y < 3; y++)
				{
				int correct = m[y, y];
				int totalRow = rowSum[y];
				double acc = totalRow > 0 ? (double) correct / totalRow * 100.0 : 0.0;

				diag += correct;

				var line = new[]
				{
					LabelName (y),
					m[y, 0].ToString (),
					m[y, 1].ToString (),
					m[y, 2].ToString (),
					correct.ToString (),
					totalRow.ToString (),
					$"{acc:0.0}%"
				};

				var color = acc >= baseline ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				t.AddColoredRow (color, line);
				}

			double accuracy = total > 0 ? (double) diag / total * 100.0 : 0.0;

			// Overall без цвета
			t.AddRow (
				"Accuracy (overall)",
				"",
				"",
				"",
				diag.ToString (),
				total.ToString (),
				$"{accuracy:0.0}%"
			);

			t.WriteToConsole ();
			}

		private static string LabelName ( int x ) => x switch
			{
				0 => "0 (down)",
				1 => "1 (flat)",
				2 => "2 (up)",
				_ => x.ToString ()
				};

		// ===== 2) Путаница по тренду (UP vs DOWN) =====

		/// <summary>
		/// Путаница только по направлению рынка:
		/// - истинный тренд: только класс 0 (down) или 2 (up), боковик (1) вообще игнорируем;
		/// - предсказанный тренд: 2 ИЛИ (1 & PredMicroUp) → UP, 0 ИЛИ (1 & PredMicroDown) → DOWN;
		/// - считаем accuracy по DOWN-дням, по UP-дням и overall;
		/// - порог цвета — 50% (выше броска монеты → зелёный, ниже → красный).
		/// </summary>
		private static void PrintTrendDirectionConfusion ( IReadOnlyList<PredictionRecord> records )
			{
			// Индекс 0 = DOWN, 1 = UP
			int[,] m = new int[2, 2];
			int[] rowSum = new int[2];
			int total = 0;

			foreach (var r in records)
				{
				// Истинный тренд — только 0 (down) и 2 (up).
				if (r.TrueLabel is < 0 or > 2) continue;

				int? trueDir = null;
				if (r.TrueLabel == 0)
					trueDir = 0; // DOWN
				else if (r.TrueLabel == 2)
					trueDir = 1; // UP
				else
					continue;    // flat (1) — вообще не учитываем

				// Предсказанный тренд с учётом micro:
				bool predUp = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool predDown = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);

				int? predDir = null;
				if (predUp && !predDown)
					predDir = 1;
				else if (predDown && !predUp)
					predDir = 0;
				else
					continue; // ситуации без явного направления не учитываем

				int y = trueDir.Value;
				int x = predDir.Value;
				m[y, x]++;
				rowSum[y]++;
				total++;
				}

			ConsoleStyler.WriteHeader ("Trend-direction confusion (DOWN vs UP)");
			var t = new TextTable ();
			t.AddHeader ("true trend", "pred DOWN", "pred UP", "correct", "total", "acc %");

			double baseline = 50.0; // «бросок монеты»

			string[] names = { "DOWN days", "UP days" };

			int diag = 0;

			for (int y = 0; y < 2; y++)
				{
				int correct = m[y, y];
				int totalRow = rowSum[y];
				double acc = totalRow > 0 ? (double) correct / totalRow * 100.0 : 0.0;

				diag += correct;

				var line = new[]
				{
					names[y],
					m[y, 0].ToString (),
					m[y, 1].ToString (),
					correct.ToString (),
					totalRow.ToString (),
					$"{acc:0.0}%"
				};

				var color = acc >= baseline ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
				t.AddColoredRow (color, line);
				}

			double overallAcc = total > 0 ? (double) diag / total * 100.0 : 0.0;
			var overallColor = overallAcc >= baseline ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;

			t.AddColoredRow (
				overallColor,
				"Accuracy (overall)",
				"",
				"",
				diag.ToString (),
				total.ToString (),
				$"{overallAcc:0.0}%"
			);

			t.WriteToConsole ();
			}

		// ===== 3) SL-модель, path-based через 1m =====

		private enum DayOutcome
			{
			None = 0,
			TpFirst = 1,
			SlFirst = 2
			}

		private static void PrintSlConfusionPathBased (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			double dailyTpPct,
			double dailySlPct,
			TimeZoneInfo nyTz )
			{
			int tp_low = 0, tp_high = 0, sl_low = 0, sl_high = 0;
			int slSaved = 0;

			// для coverage/PR-AUC
			int totalSignalDays = 0;
			int scoredDays = 0;
			var prPoints = new List<(double Score, int Label)> ();

			var m1 = sol1m.OrderBy (m => m.OpenTimeUtc).ToList ();

			foreach (var r in records)
				{
				bool goLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool goShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);
				if (!goLong && !goShort) continue;

				totalSignalDays++;

				var outcome = GetDayOutcomeFromMinutes (r, m1, dailyTpPct, dailySlPct, nyTz);
				if (outcome == DayOutcome.None)
					continue; // дни без TP/SL вообще не учитываем в confusion

				bool isSlDay = outcome == DayOutcome.SlFirst;
				bool predHigh = r.SlHighDecision;

				// считаем, что "scored" день — если модель вообще выдала probability (SlProb > 0)
				bool hasScore = r.SlProb > 0.0;
				if (hasScore)
					{
					scoredDays++;
					prPoints.Add ((r.SlProb, isSlDay ? 1 : 0));
					}

				if (!isSlDay)
					{
					if (predHigh) tp_high++; else tp_low++;
					}
				else
					{
					if (predHigh) sl_high++; else sl_low++;
					if (predHigh) slSaved++;
					}
				}

			// Confusion
			ConsoleStyler.WriteHeader ("SL-model confusion (runtime, path-based)");
			var t = new TextTable ();
			t.AddHeader ("day type", "pred LOW", "pred HIGH");
			t.AddRow ("TP-day", tp_low.ToString (), tp_high.ToString ());
			t.AddRow ("SL-day", sl_low.ToString (), sl_high.ToString ());
			t.AddRow ("SL saved (potential)", slSaved.ToString (), "");
			t.WriteToConsole ();
			Console.WriteLine ();

			// Метрики из confusion
			int tp = sl_high; // SL-day & pred HIGH
			int fn = sl_low;  // SL-day & pred LOW
			int fp = tp_high; // TP-day & pred HIGH
			int tn = tp_low;  // TP-day & pred LOW

			double tpr = (tp + fn) > 0 ? (double) tp / (tp + fn) : 0.0;              // recall
			double fpr = (fp + tn) > 0 ? (double) fp / (fp + tn) : 0.0;
			double precision = (tp + fp) > 0 ? (double) tp / (tp + fp) : 0.0;
			double recall = tpr;
			double f1 = (precision + recall) > 0 ? 2.0 * precision * recall / (precision + recall) : 0.0;

			double coverage = totalSignalDays > 0
				? (double) scoredDays / totalSignalDays
				: 0.0;

			double prAuc = prPoints.Count >= 2
				? ComputePrAuc (prPoints)
				: 0.0;

			ConsoleStyler.WriteHeader ("SL-model metrics (runtime)");
			var mTab = new TextTable ();
			mTab.AddHeader ("metric", "value");
			mTab.AddRow ("coverage (scored / signal days)", $"{coverage * 100.0:0.0}%  ({scoredDays}/{totalSignalDays})");
			mTab.AddRow ("TPR / Recall (SL-day)", $"{tpr * 100.0:0.0}%");
			mTab.AddRow ("FPR (TP-day)", $"{fpr * 100.0:0.0}%");
			mTab.AddRow ("Precision (SL-day)", $"{precision * 100.0:0.0}%");
			mTab.AddRow ("F1 (SL-day)", $"{f1:0.000}");
			mTab.AddRow ("PR-AUC (approx)", $"{prAuc:0.000}");
			mTab.WriteToConsole ();

			// === Цветной строчный summary для SL-модели ===
			PrintSlSummaryLine (coverage, tpr, fpr, precision, f1, prAuc);
			}

		/// <summary>
		/// Path-based истина по 1m:
		/// Long: TP если High >= Entry*(1+TP%) раньше, SL если Low <= Entry*(1−SL%) раньше.
		/// Short: TP если Low <= Entry*(1−TP%), SL если High >= Entry*(1+SL%).
		/// Если ни TP, ни SL — None.
		/// При одновременном срабатывании в одной минуте приоритет SL (как в PnL).
		/// Окно: [DateUtc; t_exit), t_exit = ComputeBaselineExitUtc(DateUtc, nyTz).
		/// </summary>
		private static DayOutcome GetDayOutcomeFromMinutes (
			PredictionRecord r,
			IReadOnlyList<Candle1m> allMinutes,
			double tpPct,
			double slPct,
			TimeZoneInfo nyTz )
			{
			bool goLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
			bool goShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);
			if (!goLong && !goShort) return DayOutcome.None;
			if (r.Entry <= 0) return DayOutcome.None;

			DateTime from = r.DateUtc;
			// новый baseline-горизонт вместо жёсткого +24h
			DateTime to = Windowing.ComputeBaselineExitUtc (from, nyTz);

			var dayMinutes = allMinutes
				.Where (m => m.OpenTimeUtc >= from && m.OpenTimeUtc < to)
				.ToList ();
			if (dayMinutes.Count == 0) return DayOutcome.None;

			if (goLong)
				{
				double tp = r.Entry * (1.0 + tpPct);
				double sl = slPct > 1e-9 ? r.Entry * (1.0 - slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.High >= tp;
					bool hitSl = !double.IsNaN (sl) && m.Low <= sl;
					if (!hitTp && !hitSl) continue;

					// если оба в одной минуте — считаем SL-днём
					if (hitSl) return DayOutcome.SlFirst;
					return DayOutcome.TpFirst;
					}
				}
			else // short
				{
				double tp = r.Entry * (1.0 - tpPct);
				double sl = slPct > 1e-9 ? r.Entry * (1.0 + slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.Low <= tp;
					bool hitSl = !double.IsNaN (sl) && m.High >= sl;
					if (!hitTp && !hitSl) continue;

					if (hitSl) return DayOutcome.SlFirst;
					return DayOutcome.TpFirst;
					}
				}

			return DayOutcome.None;
			}

		/// <summary>
		/// Грубая PR-AUC по точкам (score, label) с label∈{0,1}, score∈[0,1].
		/// Считаем по трапециям в координатах (recall, precision).
		/// </summary>
		private static double ComputePrAuc ( List<(double Score, int Label)> points )
			{
			if (points == null || points.Count == 0) return 0.0;

			int totalPos = points.Count (p => p.Label == 1);
			int totalNeg = points.Count (p => p.Label == 0);
			if (totalPos == 0) return 0.0;

			var sorted = points
				.OrderByDescending (p => p.Score)
				.ToList ();

			int tp = 0, fp = 0;
			double prevRecall = 0.0;
			double prevPrecision = (double) totalPos / (totalPos + totalNeg); // базовая точка
			double auc = 0.0;

			foreach (var (score, label) in sorted)
				{
				if (label == 1) tp++; else fp++;

				double recall = (double) tp / totalPos;
				double precision = (tp + fp) > 0 ? (double) tp / (tp + fp) : prevPrecision;

				double deltaR = recall - prevRecall;
				if (deltaR > 0)
					{
					auc += deltaR * (precision + prevPrecision) * 0.5;
					}

				prevRecall = recall;
				prevPrecision = precision;
				}

			return auc;
			}

		/// <summary>
		/// Одна цветная строка по SL-модели:
		/// cov, TPR, FPR, Precision, F1, PR-AUC.
		/// Условно зелёный, если TPR/F1 ок и FPR не слишком большой; иначе красный.
		/// </summary>
		private static void PrintSlSummaryLine (
			double coverage,
			double tpr,
			double fpr,
			double precision,
			double f1,
			double prAuc )
			{
			double covPct = coverage * 100.0;
			double tprPct = tpr * 100.0;
			double fprPct = fpr * 100.0;
			double precPct = precision * 100.0;

			// Очень грубые пороги, чтобы просто визуально понимать
			bool good =
				covPct >= 50.0 &&      // хотя бы половина signal-дней реально скорится
				tprPct >= 60.0 &&      // TPR ощутимо выше броска монеты
				fprPct <= 40.0 &&      // FPR не хуже 40%
				f1 >= 0.40;            // F1 не полный мусор

			var color = good ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;

			string summary =
				$"SL-model summary: " +
				$"cov={covPct:0.0}%, " +
				$"TPR={tprPct:0.0}%, " +
				$"FPR={fprPct:0.0}%, " +
				$"Prec={precPct:0.0}%, " +
				$"F1={f1:0.000}, " +
				$"PR-AUC={prAuc:0.000}";

			WriteColoredLine (color, summary);
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
