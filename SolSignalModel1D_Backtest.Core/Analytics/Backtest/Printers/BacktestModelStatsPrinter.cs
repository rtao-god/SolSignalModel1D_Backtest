using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Печать «модельных» статистик по дневной схеме/SL-модели в разрезе сегментов:
	/// - Train (DateUtc <= trainUntilUtc);
	/// - OOS (DateUtc > trainUntilUtc);
	/// - Recent (последние N дней);
	/// - Full history.
	/// Вся математика расчёта вынесена в BacktestModelStatsSnapshotBuilder /
	/// BacktestModelStatsMultiSnapshotBuilder, здесь только подготовка сегментов и вывод.
	/// </summary>
	public static class BacktestModelStatsPrinter
		{
		/// <summary>
		/// Основная точка входа:
		/// - строит мульти-снимок по Train/OOS/Recent/Full через BacktestModelStatsMultiSnapshotBuilder;
		/// - логирует метаданные (runKind, граница train, объёмы сегментов);
		/// - печатает для каждого сегмента:
		///   * дневную путаницу (3 класса),
		///   * путаницу по тренду (DOWN vs UP),
		///   * статистику SL-модели.
		/// </summary>
		public static void Print (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			double dailyTpPct,
			double dailySlPct,
			TimeZoneInfo nyTz,
			DateTime trainUntilUtc )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			ConsoleStyler.WriteHeader ("==== MODEL STATS ====");

			if (records.Count == 0)
				{
				Console.WriteLine ("[model-stats] no records, nothing to print.");
				return;
				}

			// --- 0) Логируем полный период и сортируем по дате ---
			var ordered = records
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var minDateUtc = ordered.First ().DateUtc;
			var maxDateUtc = ordered.Last ().DateUtc;

			Console.WriteLine (
				$"[model-stats] full records period = {minDateUtc:yyyy-MM-dd}..{maxDateUtc:yyyy-MM-dd}, " +
				$"totalRecords = {ordered.Count}");

			// --- 1) Граница train/OOS и размер recent-окна ---
			// Берём реальную trainUntil из верхнего уровня пайплайна (Program).
			// Это гарантирует, что train/OOS в метриках совпадает с тем, как обучалась модель.
			const int RecentDays = 240;
			var runKind = ModelRunKind.Analytics; // консольный Backtest-пайплайн — аналитический режим.

			var multi = BacktestModelStatsMultiSnapshotBuilder.Build (
				allRecords: ordered,
				sol1m: sol1m,
				nyTz: nyTz,
				dailyTpPct: dailyTpPct,
				dailySlPct: dailySlPct,
				trainUntilUtc: trainUntilUtc,
				recentDays: RecentDays,
				runKind: runKind);

			// --- 2) Общие метаданные запуска ---
			var meta = multi.Meta;

			Console.WriteLine (
				$"[model-stats] runKind={meta.RunKind}, " +
				$"trainUntil={meta.TrainUntilUtc:yyyy-MM-dd}, " +
				$"train={meta.TrainRecordsCount}, " +
				$"oos={meta.OosRecordsCount}, " +
				$"total={meta.TotalRecordsCount}, " +
				$"recentDays={meta.RecentDays}, " +
				$"recentRecords={meta.RecentRecordsCount}");

			// --- 3) Sanity-check: shuffle accuracy на recent-окне ---
			// Используем то же определение recent, что и билдер (last RecentDays),
			// но только для проверки математики accuracy.
			var fromRecentUtc = maxDateUtc.AddDays (-RecentDays);
			var recentRecords = ordered
				.Where (r => r.DateUtc >= fromRecentUtc)
				.ToList ();

			if (recentRecords.Count == 0)
				{
				recentRecords = ordered;
				}

			RunShuffleSanityTest (recentRecords);

			// --- 4) Печать сегментов в стабильном порядке:
			// OOS → Train → Recent → Full.
			PrintSegmentIfExists (multi, ModelStatsSegmentKind.OosOnly, "OOS segment");
			PrintSegmentIfExists (multi, ModelStatsSegmentKind.TrainOnly, "Train segment");
			PrintSegmentIfExists (multi, ModelStatsSegmentKind.RecentWindow, "Recent segment");
			PrintSegmentIfExists (multi, ModelStatsSegmentKind.FullHistory, "Full-history segment");
			}

		private static void PrintSegmentIfExists (
			BacktestModelStatsMultiSnapshot multi,
			ModelStatsSegmentKind kind,
			string segmentTitle )
			{
			var segment = multi.Segments
				.FirstOrDefault (s => s.Kind == kind);

			if (segment == null)
				return;

			ConsoleStyler.WriteHeader (
				$"{segmentTitle}: {segment.Label} " +
				$"[{segment.FromDateUtc:yyyy-MM-dd}..{segment.ToDateUtc:yyyy-MM-dd}, " +
				$"records={segment.RecordsCount}]");

			// 1) Обычная 3-классовая путаница
			PrintDailyConfusion (segment.Stats.Daily, scopeLabel: segment.Label);
			Console.WriteLine ();

			// 2) Путаница по тренду (UP vs DOWN)
			PrintTrendDirectionConfusion (segment.Stats.Trend, scopeLabel: segment.Label);
			Console.WriteLine ();

			// 3) SL-модель (path-based по 1m)
			PrintSlStats (segment.Stats.Sl);
			Console.WriteLine ();
			}

		// ===== 1) Дневная путаница (3 класса) =====

		private static void PrintDailyConfusion ( DailyConfusionStats daily, string? scopeLabel = null )
			{
			var title = scopeLabel == null
				? "Daily label confusion (3-class)"
				: $"Daily label confusion (3-class) [{scopeLabel}]";

			ConsoleStyler.WriteHeader (title);

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
		private static void PrintTrendDirectionConfusion ( TrendDirectionStats trend, string? scopeLabel = null )
			{
			var title = scopeLabel == null
				? "Trend-direction confusion (DOWN vs UP)"
				: $"Trend-direction confusion (DOWN vs UP) [{scopeLabel}]";

			ConsoleStyler.WriteHeader (title);

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

		/// <summary>
		/// Простейший sanity-тест:
		/// - берёт текущие (TrueLabel, PredLabel) по recent-окну;
		/// - случайно перемешивает TrueLabel между днями;
		/// - считает accuracy для той же PredLabel.
		/// Если расчёт метрик нормальный, accuracy на shuffled должна быть ~33% для 3 классов.
		/// Если там тоже ~70–80%, значит где-то баг в логике подсчёта.
		/// </summary>
		private static void RunShuffleSanityTest ( IReadOnlyList<PredictionRecord> recordsForMetrics )
			{
			if (recordsForMetrics == null || recordsForMetrics.Count == 0)
				{
				Console.WriteLine ("[model-stats][shuffle] no records for sanity test – skipped.");
				return;
				}

			// Берём только валидные пары 0/1/2.
			var filtered = recordsForMetrics
				.Where (r => r.TrueLabel is >= 0 and <= 2
							&& r.PredLabel is >= 0 and <= 2)
				.ToList ();

			if (filtered.Count == 0)
				{
				Console.WriteLine ("[model-stats][shuffle] no valid (true,pred) pairs – skipped.");
				return;
				}

			var labels = filtered
				.Select (r => r.TrueLabel)
				.ToArray ();

			// Фишер–Йетс с фиксированным сидом, чтобы результат был воспроизводим.
			var rng = new Random (123);

			for (int i = labels.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(labels[i], labels[j]) = (labels[j], labels[i]);
				}

			int diag = 0;
			int n = filtered.Count;

			for (int i = 0; i < n; i++)
				{
				if (labels[i] == filtered[i].PredLabel)
					{
					diag++;
					}
				}

			double accPct = (double) diag / n * 100.0;

			Console.WriteLine (
				$"[model-stats][shuffle] sanity acc on shuffled labels = {accPct:0.0}% (n={n})");
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
