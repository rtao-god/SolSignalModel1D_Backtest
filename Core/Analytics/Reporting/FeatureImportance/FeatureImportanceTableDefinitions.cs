using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.Analytics.Reporting;
// TODO: Поменять название папки FeatureImportance на нормальное
namespace SolSignalModel1D_Backtest.Core.Analytics.FeatureImportance.Reporting
	{
	/// <summary>
	/// Табличные определения для PFI-метрик.
	/// Здесь описывается, какие колонки есть у таблицы PFI
	/// и с какого уровня детализации (Simple/Technical) их показывать.
	/// Логика расчёта метрик остаётся в PFI-ядре, здесь только "представление".
	/// </summary>
	public static class FeatureImportanceTableDefinitions
		{
		/// <summary>
		/// Универсальное определение таблицы PFI по одной модели:
		/// строки = фичи (FeatureStats), столбцы = метрики Importance / direction.
		/// Один и тот же definition используется и для Simple, и для Technical —
		/// разница только в фильтре по MinLevel.
		/// </summary>
		public static MetricTableDefinition<FeatureStats> PerModelFeatureStats { get; } =
			new MetricTableDefinition<FeatureStats> (
				tableKey: "pfi_per_model",
				title: "PFI по фичам (одна модель)",
				columns: new List<MetricColumnDefinition<FeatureStats>>
				{
                    // # (индекс слота)
                    new MetricColumnDefinition<FeatureStats>(
						key: "index",
						simpleTitle: "#",
						technicalTitle: "Index",
						minLevel: TableDetailLevel.Simple,
						valueSelector: s => s.Index.ToString()
					),

                    // Имя фичи
                    new MetricColumnDefinition<FeatureStats>(
						key: "name",
						simpleTitle: "Фича",
						technicalTitle: "FeatureName",
						minLevel: TableDetailLevel.Simple,
						valueSelector: s => s.Name ?? string.Empty
					),

                    // Важность по AUC (в p.p. или как есть)
                    new MetricColumnDefinition<FeatureStats>(
						key: "importance_auc",
						simpleTitle: "Важность (ΔAUC)",
						technicalTitle: "ImportanceAuc (abs ΔAUC)",
						minLevel: TableDetailLevel.Simple,
						valueSelector: s => $"{s.ImportanceAuc * 100.0:0.00}"
					),

                    // Сырый ΔAUC с подписью
                    new MetricColumnDefinition<FeatureStats>(
						key: "delta_auc",
						simpleTitle: "ΔAUC (сырое)",
						technicalTitle: "DeltaAuc (baseline - perm)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: s => $"{s.DeltaAuc * 100.0:0.00}"
					),

                    // ΔMean = MeanPos - MeanNeg — это уже "направление" фичи.
                    new MetricColumnDefinition<FeatureStats>(
						key: "delta_mean",
						simpleTitle: "ΔMean (1-0)",
						technicalTitle: "MeanPos - MeanNeg",
						minLevel: TableDetailLevel.Simple,
						valueSelector: s => double.IsNaN(s.DeltaMean) ? "NaN" : $"{s.DeltaMean:0.0000}"
					),

                    // Среднее по положительному классу
                    new MetricColumnDefinition<FeatureStats>(
						key: "mean_pos",
						simpleTitle: "Mean[1]",
						technicalTitle: "MeanPos (Label=1)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: s => double.IsNaN(s.MeanPos) ? "NaN" : $"{s.MeanPos:0.0000}"
					),

                    // Среднее по отрицательному классу
                    new MetricColumnDefinition<FeatureStats>(
						key: "mean_neg",
						simpleTitle: "Mean[0]",
						technicalTitle: "MeanNeg (Label=0)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: s => double.IsNaN(s.MeanNeg) ? "NaN" : $"{s.MeanNeg:0.0000}"
					),

                    // Корреляция со скором модели — основная "интуитивная" метрика.
                    new MetricColumnDefinition<FeatureStats>(
						key: "corr_score",
						simpleTitle: "Corr(score)",
						technicalTitle: "CorrScore (Pearson)",
						minLevel: TableDetailLevel.Simple,
						valueSelector: s => $"{s.CorrScore:0.000}"
					),

                    // Корреляция с таргетом — больше технарская штука.
                    new MetricColumnDefinition<FeatureStats>(
						key: "corr_label",
						simpleTitle: "Corr(label)",
						technicalTitle: "CorrLabel (Pearson)",
						minLevel: TableDetailLevel.Technical,
						valueSelector: s => $"{s.CorrLabel:0.000}"
					),

                    // Поддержка по классам — лучше показать хотя бы агрегированно даже в Simple.
                    new MetricColumnDefinition<FeatureStats>(
						key: "support",
						simpleTitle: "Support (pos/neg)",
						technicalTitle: "CountPos / CountNeg",
						minLevel: TableDetailLevel.Simple,
						valueSelector: s => $"{s.CountPos}/{s.CountNeg}"
					)
				});
		}
	}
