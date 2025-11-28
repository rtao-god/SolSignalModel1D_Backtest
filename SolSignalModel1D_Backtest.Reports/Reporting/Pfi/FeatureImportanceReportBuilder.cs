using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Reporting.Pfi
	{
	/// <summary>
	/// Построитель отчётов по PFI:
	/// - умеет строить по одному TableSection на модель;
	/// - умеет упаковывать их в единый ReportDocument.
	/// Отчёты используют FeatureImportanceSnapshots, которые уже собираются
	/// через FeatureImportanceAnalyzer.LogBinaryFeatureImportance(...)
	/// </summary>
	public static class FeatureImportanceReportBuilder
		{
		/// <summary>
		/// Строит список таблиц PFI (по одной таблице на модель) для заданного уровня детализации.
		/// Каждая таблица содержит все фичи модели (включая нулевые importance),
		/// но состав колонок определяется level (Simple/Technical).
		/// </summary>
		public static List<TableSection> BuildPerModelTables (
			IReadOnlyList<FeatureImportanceSnapshot> snapshots,
			TableDetailLevel level )
			{
			if (snapshots == null) throw new ArgumentNullException (nameof (snapshots));

			var result = new List<TableSection> (snapshots.Count);

			foreach (var snap in snapshots)
				{
				// Человекочитаемый заголовок для конкретной модели:
				// включаем туда тег и baseline AUC, чтобы на UI сразу было понятно, что за модель.
				var title = $"{snap.Tag} (AUC={snap.BaselineAuc:F4})";

				var table = MetricTableBuilder.BuildTable (
					FeatureImportanceTableDefinitions.PerModelFeatureStats,
					snap.Stats,
					level,
					explicitTitle: title);

				result.Add (table);
				}

			return result;
			}

		/// <summary>
		/// Строит единый ReportDocument с PFI-таблицами по всем моделям.
		/// Это удобный формат для:
		/// - сохранения через ReportStorage;
		/// - отдачи на фронт в виде одного "документа".
		/// </summary>
		public static ReportDocument BuildPerModelReport (
			IReadOnlyList<FeatureImportanceSnapshot> snapshots,
			TableDetailLevel level,
			string? explicitTitle = null )
			{
			if (snapshots == null) throw new ArgumentNullException (nameof (snapshots));

			var now = DateTime.UtcNow;

			// Генерируем предсказуемый Id, чтобы можно было хранить снапшоты PFI так же, как backtest_summary.
			var id = $"pfi-per-model-{now:yyyyMMdd_HHmmss}";

			var doc = new ReportDocument
				{
				Id = id,
				Kind = "pfi_per_model",
				Title = explicitTitle ?? "PFI по моделям (binary)",
				GeneratedAtUtc = now,
				KeyValueSections = new List<KeyValueSection> (),
				TableSections = new List<TableSection> (),
				TextSections = new List<TextSection> ()
				};

			var tables = BuildPerModelTables (snapshots, level);
			doc.TableSections.AddRange (tables);

			return doc;
			}
		}
	}
