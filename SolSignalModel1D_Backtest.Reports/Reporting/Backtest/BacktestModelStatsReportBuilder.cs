using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Backtest.Snapshots;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Reporting.Backtest
	{
	/// <summary>
	/// Строит ReportDocument с модельными статистиками бэктеста
	/// на основе BacktestModelStatsSnapshot.
	/// Никакой новой математики не добавляет — только форматирует данные
	/// для фронта в виде key-value и таблиц (simple/technical).
	/// </summary>
	public static class BacktestModelStatsReportBuilder
		{
		/// <summary>
		/// Строит отчёт по модельным статистикам.
		/// Предполагается, что снимок получен через BacktestModelStatsSnapshotBuilder.Compute(...).
		/// </summary>
		public static ReportDocument Build ( BacktestModelStatsSnapshot snapshot )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));

			var doc = new ReportDocument
				{
				Id = $"backtest-model-stats-{snapshot.ToDateUtc:yyyyMMdd}",
				Kind = "backtest_model_stats",
				Title = "Модельные статистики бэктеста (SOLUSDT)",
				GeneratedAtUtc = DateTime.UtcNow
				};

			// === Общая метаинформация по окну, в котором считались статистики ===
			var metaSection = new KeyValueSection
				{
				Title = "Параметры модельных статистик"
				};

			metaSection.Items.Add (new KeyValueItem
				{
				Key = "FromDateUtc",
				Value = snapshot.FromDateUtc.ToString ("O")
				});

			metaSection.Items.Add (new KeyValueItem
				{
				Key = "ToDateUtc",
				Value = snapshot.ToDateUtc.ToString ("O")
				});

			metaSection.Items.Add (new KeyValueItem
				{
				Key = "SlSignalDays",
				Value = snapshot.Sl.Confusion.TotalSignalDays.ToString ()
				});

			metaSection.Items.Add (new KeyValueItem
				{
				Key = "SlOutcomeDays",
				Value = snapshot.Sl.Confusion.TotalOutcomeDays.ToString ()
				});

			doc.KeyValueSections.Add (metaSection);

			// === Daily confusion: бизнес (summary) + технарь (матрица) ===
			AddDailyConfusionSections (doc, snapshot.Daily);

			// === Trend-direction confusion: simple / technical ===
			var trendSimple = MetricTableBuilder.BuildTable (
				BacktestModelStatsTableDefinitions.TrendConfusion,
				snapshot.Trend.Rows,
				TableDetailLevel.Simple,
				explicitTitle: "Trend-direction confusion (упрощённо)");

			var trendTechnical = MetricTableBuilder.BuildTable (
				BacktestModelStatsTableDefinitions.TrendConfusion,
				snapshot.Trend.Rows,
				TableDetailLevel.Technical,
				explicitTitle: "Trend-direction confusion (технически)");

			doc.TableSections.Add (trendSimple);
			doc.TableSections.Add (trendTechnical);

			// === SL confusion (аналог консольной таблицы) ===
			var slConf = snapshot.Sl.Confusion;

			var slConfSection = new TableSection
				{
				Title = "SL-model confusion (runtime, path-based)"
				};

			slConfSection.Columns.AddRange (new[]
			{
				"day type",
				"pred LOW",
				"pred HIGH"
			});

			slConfSection.Rows.Add (new List<string>
			{
				"TP-day",
				slConf.TpLow.ToString(),
				slConf.TpHigh.ToString()
			});

			slConfSection.Rows.Add (new List<string>
			{
				"SL-day",
				slConf.SlLow.ToString(),
				slConf.SlHigh.ToString()
			});

			slConfSection.Rows.Add (new List<string>
			{
				"SL saved (potential)",
				slConf.SlSaved.ToString(),
				string.Empty
			});

			doc.TableSections.Add (slConfSection);

			// === SL metrics (как в консоли) ===
			var slMetrics = snapshot.Sl.Metrics;

			var slMetricsSection = new TableSection
				{
				Title = "SL-model metrics (runtime)"
				};

			slMetricsSection.Columns.AddRange (new[]
			{
				"metric",
				"value"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"coverage (scored / signal days)",
				$"{slMetrics.Coverage * 100.0:0.0}%  ({slConf.ScoredDays}/{slConf.TotalSignalDays})"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"TPR / Recall (SL-day)",
				$"{slMetrics.Tpr * 100.0:0.0}%"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"FPR (TP-day)",
				$"{slMetrics.Fpr * 100.0:0.0}%"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"Precision (SL-day)",
				$"{slMetrics.Precision * 100.0:0.0}%"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"F1 (SL-day)",
				$"{slMetrics.F1:0.000}"
			});

			slMetricsSection.Rows.Add (new List<string>
			{
				"PR-AUC (approx)",
				$"{slMetrics.PrAuc:0.000}"
			});

			doc.TableSections.Add (slMetricsSection);

			// === SL threshold sweep: simple / technical (если есть данные) ===
			if (snapshot.Sl.Thresholds.Count > 0)
				{
				var thrSimple = MetricTableBuilder.BuildTable (
					BacktestModelStatsTableDefinitions.SlThresholdSweep,
					snapshot.Sl.Thresholds,
					TableDetailLevel.Simple,
					explicitTitle: "SL threshold sweep (упрощённо)");

				var thrTechnical = MetricTableBuilder.BuildTable (
					BacktestModelStatsTableDefinitions.SlThresholdSweep,
					snapshot.Sl.Thresholds,
					TableDetailLevel.Technical,
					explicitTitle: "SL threshold sweep (технически)");

				doc.TableSections.Add (thrSimple);
				doc.TableSections.Add (thrTechnical);
				}

			return doc;
			}

		/// <summary>
		/// Добавляет в документ две секции по дневной путанице:
		/// - бизнес-режим (человеческое summary по классам);
		/// - технарский режим (матрица TRUE x PRED, как confusion).
		/// </summary>
		private static void AddDailyConfusionSections ( ReportDocument doc, DailyConfusionStats daily )
			{
			if (doc == null) throw new ArgumentNullException (nameof (doc));
			if (daily == null) throw new ArgumentNullException (nameof (daily));

			// Бизнес-режим: компактные текстовые summary по каждому классу.
			var summarySection = BuildDailyBusinessSummarySection (daily);
			if (summarySection != null)
				{
				doc.TableSections.Add (summarySection);
				}

			// Технарский режим: нормальная confusion-матрица с процентами и количеством.
			var matrixSection = BuildDailyTechnicalMatrixSection (daily);
			if (matrixSection != null)
				{
				doc.TableSections.Add (matrixSection);
				}
			}

		/// <summary>
		/// Строит "человеческое" описание по классам в виде таблицы:
		/// Class + Summary.
		/// </summary>
		private static TableSection? BuildDailyBusinessSummarySection ( DailyConfusionStats daily )
			{
			if (daily.Rows == null || daily.Rows.Count == 0)
				return null;

			var section = new TableSection
				{
				Title = "Daily label summary (business)"
				};

			section.Columns.AddRange (new[]
			{
				"Class",
				"Summary"
			});

			var down = FindDailyRowByLabel (daily.Rows, 0);
			var flat = FindDailyRowByLabel (daily.Rows, 1);
			var up = FindDailyRowByLabel (daily.Rows, 2);

			if (down != null && down.Total > 0)
				{
				section.Rows.Add (new List<string>
					{
					"DOWN",
					BuildDailyClassSummary (
						total: down.Total,
						correct: down.Correct,
						missToDown: 0,
						missToFlat: down.Pred1,
						missToUp: down.Pred2)
					});
				}

			if (flat != null && flat.Total > 0)
				{
				section.Rows.Add (new List<string>
					{
					"FLAT",
					BuildDailyClassSummary (
						total: flat.Total,
						correct: flat.Correct,
						missToDown: flat.Pred0,
						missToFlat: 0,
						missToUp: flat.Pred2)
					});
				}

			if (up != null && up.Total > 0)
				{
				section.Rows.Add (new List<string>
					{
					"UP",
					BuildDailyClassSummary (
						total: up.Total,
						correct: up.Correct,
						missToDown: up.Pred0,
						missToFlat: up.Pred1,
						missToUp: 0)
					});
				}

			// Общий summary по всем классам.
			if (daily.OverallTotal > 0)
				{
				var overallSummary =
					$"Всего {daily.OverallTotal} дней. " +
					$"Попаданий: {daily.OverallCorrect} ({daily.OverallAccuracyPct:0.0}%).";

				section.Rows.Add (new List<string>
					{
					"Overall",
					overallSummary
					});
				}

			return section;
			}

		/// <summary>
		/// Строит технарскую confusion-матрицу TRUE x PRED с процентами и количеством.
		/// </summary>
		private static TableSection? BuildDailyTechnicalMatrixSection ( DailyConfusionStats daily )
			{
			if (daily.Rows == null || daily.Rows.Count == 0)
				return null;

			var section = new TableSection
				{
				Title = "Daily label confusion (3-class, technical)"
				};

			section.Columns.AddRange (new[]
			{
				"TRUE",
				"Pred DOWN",
				"Pred FLAT",
				"Pred UP",
				"Hit %"
			});

			var down = FindDailyRowByLabel (daily.Rows, 0);
			var flat = FindDailyRowByLabel (daily.Rows, 1);
			var up = FindDailyRowByLabel (daily.Rows, 2);

			if (down != null && down.Total > 0)
				{
				section.Rows.Add (BuildMatrixRow ("DOWN", down));
				}

			if (flat != null && flat.Total > 0)
				{
				section.Rows.Add (BuildMatrixRow ("FLAT", flat));
				}

			if (up != null && up.Total > 0)
				{
				section.Rows.Add (BuildMatrixRow ("UP", up));
				}

			// Общий accuracy по всем классам.
			if (daily.OverallTotal > 0)
				{
				var overall = $"{daily.OverallAccuracyPct:0.0}% ({daily.OverallCorrect} / {daily.OverallTotal})";

				section.Rows.Add (new List<string>
					{
					"Overall accuracy",
					string.Empty,
					string.Empty,
					string.Empty,
					overall
					});
				}

			return section;
			}

		/// <summary>
		/// Находит строку дневной статистики по true label (0/1/2).
		/// </summary>
		private static DailyClassStatsRow? FindDailyRowByLabel ( IReadOnlyList<DailyClassStatsRow> rows, int trueLabel )
			{
			for (int i = 0; i < rows.Count; i++)
				{
				var row = rows[i];
				if (row.TrueLabel == trueLabel)
					return row;
				}

			return null;
			}

		/// <summary>
		/// Формирует человеческое summary по одному классу:
		/// всего дней, попадания, промахи и куда уезжают промахи.
		/// </summary>
		private static string BuildDailyClassSummary (
			int total,
			int correct,
			int missToDown,
			int missToFlat,
			int missToUp )
			{
			if (total <= 0)
				return "Недостаточно данных по этому классу.";

			int misses = total - correct;

			double accPct = (double) correct / total * 100.0;
			double missPct = 100.0 - accPct;

			double missDownPct = (double) missToDown / total * 100.0;
			double missFlatPct = (double) missToFlat / total * 100.0;
			double missUpPct = (double) missToUp / total * 100.0;

			return
				$"Всего {total} дней. " +
				$"Попаданий: {correct} ({accPct:0.0}%). " +
				$"Промахов: {misses} ({missPct:0.0}%). " +
				$"Ошибки: " +
				$"DOWN {missDownPct:0.0}% ({missToDown}), " +
				$"FLAT {missFlatPct:0.0}% ({missToFlat}), " +
				$"UP {missUpPct:0.0}% ({missToUp}).";
			}

		/// <summary>
		/// Формирует строку матрицы TRUE x PRED для одного класса.
		/// </summary>
		private static List<string> BuildMatrixRow ( string trueName, DailyClassStatsRow row )
			{
			int total = row.Total;

			string Format ( int count )
				{
				if (total <= 0)
					return "—";

				double pct = (double) count / total * 100.0;
				return $"{pct:0.0}% ({count})";
				}

			return new List<string>
				{
				trueName,
				Format (row.Pred0),
				Format (row.Pred1),
				Format (row.Pred2),
				$"{row.AccuracyPct:0.0}%"
				};
			}
		}
	}
