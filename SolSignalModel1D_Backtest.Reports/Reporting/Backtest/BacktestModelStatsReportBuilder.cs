using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Snapshots;
using SolSignalModel1D_Backtest.Reports.Model;
using SolSignalModel1D_Backtest.Reports.Reporting;
using SolSignalModel1D_Backtest.Reports.Reporting.Backtest;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Reports.Backtest.Reports
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

			// === Daily confusion: simple / technical ===
			var dailySimple = MetricTableBuilder.BuildTable (
				BacktestModelStatsTableDefinitions.DailyConfusion,
				snapshot.Daily.Rows,
				TableDetailLevel.Simple,
				explicitTitle: "Daily confusion (упрощённо)");

			var dailyTechnical = MetricTableBuilder.BuildTable (
				BacktestModelStatsTableDefinitions.DailyConfusion,
				snapshot.Daily.Rows,
				TableDetailLevel.Technical,
				explicitTitle: "Daily confusion (технически)");

			doc.TableSections.Add (dailySimple);
			doc.TableSections.Add (dailyTechnical);

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
		}
	}
