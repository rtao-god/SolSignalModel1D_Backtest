using SolSignalModel1D_Backtest.Core.Backtest.Snapshots;
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
	/// Теперь расчёты вынесены в BacktestModelStatsSnapshotBuilder, а этот класс отвечает только за вывод.
	/// </summary>
	public static class BacktestModelStatsPrinter
		{
		/// <summary>
		/// Основная точка входа:
		/// - Daily confusion по классам (0/1/2);
		/// - Trend-confusion по направлению (DOWN vs UP);
		/// - SL-model confusion + метрики (TPR/FPR/Precision/Recall/F1/PR-AUC, coverage)
		///   + доп. sweep по порогам вероятности SL.
		/// Вся математика берётся из BacktestModelStatsSnapshotBuilder.
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

			// Считаем снимок модельных статистик.
			var snapshot = BacktestModelStatsSnapshotBuilder.Compute (
				records,
				sol1m,
				dailyTpPct,
				dailySlPct,
				nyTz);

			// 1) Обычная 3-классовая путаница
			PrintDailyConfusion (snapshot.Daily);
			Console.WriteLine ();

			// 2) Путаница по тренду (UP vs DOWN)
			PrintTrendDirectionConfusion (snapshot.Trend);
			Console.WriteLine ();

			// 3) SL-модель (path-based по 1m) в том же окне, что и таргеты/PnL
			PrintSlStats (snapshot.Sl);
			Console.WriteLine ();
			}

		// ===== 1) Дневная путаница (3 класса) =====

		private static void PrintDailyConfusion ( DailyConfusionStats daily )
			{
			ConsoleStyler.WriteHeader ("Daily label confusion (3-class)");

			var t = new TextTable ();
			t.AddHeader ("true label", "pred 0", "pred 1", "pred 2", "correct", "total", "acc %");

			// «Бросок монеты» для 3 классов — 1/3 ≈ 33.3%
			double baseline = 100.0 / 3.0;

			foreach (var row in daily.Rows)
				{
				var line = new[]
				{
					row.LabelName,
					row.Pred0.ToString(),
					row.Pred1.ToString(),
					row.Pred2.ToString(),
					row.Correct.ToString(),
					row.Total.ToString(),
					$"{row.AccuracyPct:0.0}%"
				};

				var color = row.AccuracyPct >= baseline
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (color, line);
				}

			// Overall без цвета
			t.AddRow (
				"Accuracy (overall)",
				"",
				"",
				"",
				daily.OverallCorrect.ToString (),
				daily.OverallTotal.ToString (),
				$"{daily.OverallAccuracyPct:0.0}%"
			);

			t.WriteToConsole ();
			}

		// ===== 2) Путаница по тренду (UP vs DOWN) =====

		/// <summary>
		/// Печать путаницы только по направлению рынка:
		/// использует уже посчитанные TrendDirectionStats.
		/// </summary>
		private static void PrintTrendDirectionConfusion ( TrendDirectionStats trend )
			{
			ConsoleStyler.WriteHeader ("Trend-direction confusion (DOWN vs UP)");

			var t = new TextTable ();
			t.AddHeader ("true trend", "pred DOWN", "pred UP", "correct", "total", "acc %");

			double baseline = 50.0; // «бросок монеты»

			foreach (var row in trend.Rows)
				{
				var line = new[]
				{
					row.Name,
					row.PredDown.ToString(),
					row.PredUp.ToString(),
					row.Correct.ToString(),
					row.Total.ToString(),
					$"{row.AccuracyPct:0.0}%"
				};

				var color = row.AccuracyPct >= baseline
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (color, line);
				}

			var overallColor = trend.OverallAccuracyPct >= baseline
				? ConsoleStyler.GoodColor
				: ConsoleStyler.BadColor;

			t.AddColoredRow (
				overallColor,
				"Accuracy (overall)",
				"",
				"",
				trend.OverallCorrect.ToString (),
				trend.OverallTotal.ToString (),
				$"{trend.OverallAccuracyPct:0.0}%"
			);

			t.WriteToConsole ();
			}

		// ===== 3) SL-модель, path-based через 1m =====

		/// <summary>
		/// Печать полной статистики SL-модели:
		/// - confusion-таблица;
		/// - основные метрики;
		/// - цветной summary;
		/// - sweep по порогам.
		/// Все данные берутся из SlStats.
		/// </summary>
		private static void PrintSlStats ( SlStats sl )
			{
			var confusion = sl.Confusion;
			var metrics = sl.Metrics;

			// Confusion
			ConsoleStyler.WriteHeader ("SL-model confusion (runtime, path-based)");

			var t = new TextTable ();
			t.AddHeader ("day type", "pred LOW", "pred HIGH");
			t.AddRow ("TP-day", confusion.TpLow.ToString (), confusion.TpHigh.ToString ());
			t.AddRow ("SL-day", confusion.SlLow.ToString (), confusion.SlHigh.ToString ());
			t.AddRow ("SL saved (potential)", confusion.SlSaved.ToString (), "");
			t.WriteToConsole ();
			Console.WriteLine ();

			// Метрики
			ConsoleStyler.WriteHeader ("SL-model metrics (runtime)");
			var mTab = new TextTable ();
			mTab.AddHeader ("metric", "value");
			mTab.AddRow ("coverage (scored / signal days)", $"{metrics.Coverage * 100.0:0.0}%  ({confusion.ScoredDays}/{confusion.TotalSignalDays})");
			mTab.AddRow ("TPR / Recall (SL-day)", $"{metrics.Tpr * 100.0:0.0}%");
			mTab.AddRow ("FPR (TP-day)", $"{metrics.Fpr * 100.0:0.0}%");
			mTab.AddRow ("Precision (SL-day)", $"{metrics.Precision * 100.0:0.0}%");
			mTab.AddRow ("F1 (SL-day)", $"{metrics.F1:0.000}");
			mTab.AddRow ("PR-AUC (approx)", $"{metrics.PrAuc:0.000}");
			mTab.WriteToConsole ();

			// Цветной summary — логика порогов/цвета сохранена.
			PrintSlSummaryLine (
				metrics.Coverage,
				metrics.Tpr,
				metrics.Fpr,
				metrics.Precision,
				metrics.F1,
				metrics.PrAuc);

			// Sweep по порогам
			PrintSlThresholdSweep (sl);
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

		/// <summary>
		/// Sweep по порогам вероятности SL-модели на OOS-наборе.
		/// Печатает таблицу:
		/// thr, TPR(SL), FPR(TP), pred HIGH %, high / total.
		/// Данные берутся из SlStats.Thresholds и SlStats.Confusion.
		/// </summary>
		private static void PrintSlThresholdSweep ( SlStats sl )
			{
			ConsoleStyler.WriteHeader ("SL threshold sweep (runtime)");

			var thresholds = sl.Thresholds;
			var confusion = sl.Confusion;

			if (thresholds == null || thresholds.Count == 0)
				{
				Console.WriteLine ("[sl-thr] no days with both TP/SL outcome and SlProb > 0 – sweep skipped.");
				return;
				}

			Console.WriteLine ($"[sl-thr] base set: totalDays={confusion.TotalOutcomeDays}, SL-days={confusion.TotalSlDays}, TP-days={confusion.TotalTpDays}");

			var t = new TextTable ();
			t.AddHeader ("thr", "TPR(SL)", "FPR(TP)", "pred HIGH %", "high / total");

			foreach (var row in thresholds)
				{
				var cells = new[]
				{
					row.Threshold.ToString("0.00"),
					$"{row.TprPct:0.0}%",
					$"{row.FprPct:0.0}%",
					$"{row.PredHighPct:0.0}%",
					$"{row.HighTotal}/{row.TotalDays}"
				};

				var color = row.IsGood
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (color, cells);
				}

			t.WriteToConsole ();
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
