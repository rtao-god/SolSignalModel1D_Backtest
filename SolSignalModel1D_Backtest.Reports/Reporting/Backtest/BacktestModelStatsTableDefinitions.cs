using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats;

namespace SolSignalModel1D_Backtest.Reports.Reporting.Backtest
	{
	/// <summary>
	/// Табличные определения для модельных статистик бэктеста.
	/// Используются MetricTableBuilder + TableDetailLevel (Simple / Technical).
	/// </summary>
	public static class BacktestModelStatsTableDefinitions
		{
		/// <summary>
		/// Daily confusion (3 класса).
		/// Simple: класс + accuracy, Technical: полный набор колонок.
		/// </summary>
		public static readonly MetricTableDefinition<DailyClassStatsRow> DailyConfusion =
			new MetricTableDefinition<DailyClassStatsRow> (
				tableKey: "model_daily_confusion",
				title: "Daily label confusion (3-class)",
				columns: new[]
				{
					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "label",
						simpleTitle: "Класс",
						technicalTitle: "True label",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.LabelName),

					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "pred0",
						simpleTitle: "pred 0",
						technicalTitle: "Pred 0 (count)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Pred0.ToString()),

					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "pred1",
						simpleTitle: "pred 1",
						technicalTitle: "Pred 1 (count)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Pred1.ToString()),

					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "pred2",
						simpleTitle: "pred 2",
						technicalTitle: "Pred 2 (count)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Pred2.ToString()),

					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "correct",
						simpleTitle: "correct",
						technicalTitle: "Correct",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Correct.ToString()),

					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "total",
						simpleTitle: "total",
						technicalTitle: "Total",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Total.ToString()),

					new MetricColumnDefinition<DailyClassStatsRow> (
						key: "acc",
						simpleTitle: "Точность, %",
						technicalTitle: "Accuracy, %",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.AccuracyPct.ToString ("0.0"))
				});

		/// <summary>
		/// Trend-direction confusion (DOWN vs UP).
		/// Simple: только имя ряда и accuracy, Technical: добавляются счётчики.
		/// </summary>
		public static readonly MetricTableDefinition<TrendDirectionStatsRow> TrendConfusion =
			new MetricTableDefinition<TrendDirectionStatsRow> (
				tableKey: "model_trend_confusion",
				title: "Trend-direction confusion (DOWN vs UP)",
				columns: new[]
				{
					new MetricColumnDefinition<TrendDirectionStatsRow> (
						key: "name",
						simpleTitle: "Тип дня",
						technicalTitle: "True trend",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.Name),

					new MetricColumnDefinition<TrendDirectionStatsRow> (
						key: "pred_down",
						simpleTitle: "pred DOWN",
						technicalTitle: "Pred DOWN (count)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.PredDown.ToString()),

					new MetricColumnDefinition<TrendDirectionStatsRow> (
						key: "pred_up",
						simpleTitle: "pred UP",
						technicalTitle: "Pred UP (count)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.PredUp.ToString()),

					new MetricColumnDefinition<TrendDirectionStatsRow> (
						key: "correct",
						simpleTitle: "correct",
						technicalTitle: "Correct",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Correct.ToString()),

					new MetricColumnDefinition<TrendDirectionStatsRow> (
						key: "total",
						simpleTitle: "total",
						technicalTitle: "Total",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.Total.ToString()),

					new MetricColumnDefinition<TrendDirectionStatsRow> (
						key: "acc",
						simpleTitle: "Точность, %",
						technicalTitle: "Accuracy, %",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.AccuracyPct.ToString ("0.0"))
				});

		/// <summary>
		/// Sweep по порогам SL-модели.
		/// Simple: порог, TPR, pred HIGH %; Technical: добавляем FPR и high/total.
		/// </summary>
		public static readonly MetricTableDefinition<SlThresholdStatsRow> SlThresholdSweep =
			new MetricTableDefinition<SlThresholdStatsRow> (
				tableKey: "sl_threshold_sweep",
				title: "SL threshold sweep (runtime)",
				columns: new[]
				{
					new MetricColumnDefinition<SlThresholdStatsRow> (
						key: "thr",
						simpleTitle: "Порог",
						technicalTitle: "Threshold",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.Threshold.ToString ("0.00")),

					new MetricColumnDefinition<SlThresholdStatsRow> (
						key: "tpr",
						simpleTitle: "TPR(SL), %",
						technicalTitle: "TPR(SL), %",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.TprPct.ToString ("0.0")),

					new MetricColumnDefinition<SlThresholdStatsRow> (
						key: "fpr",
						simpleTitle: "FPR(TP), %",
						technicalTitle: "FPR(TP), %",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => r.FprPct.ToString ("0.0")),

					new MetricColumnDefinition<SlThresholdStatsRow> (
						key: "pred_high_pct",
						simpleTitle: "pred HIGH, %",
						technicalTitle: "Pred HIGH, %",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.PredHighPct.ToString ("0.0")),

					new MetricColumnDefinition<SlThresholdStatsRow> (
						key: "high_total",
						simpleTitle: "high / total",
						technicalTitle: "High / total",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => $"{r.HighTotal}/{r.TotalDays}")
				});
		}
	}
