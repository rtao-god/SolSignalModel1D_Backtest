using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Reporting.Ml
	{
	/// <summary>
	/// Строит отчёт по статистике моделей (уровень "модель", а не "фича"):
	/// - tag модели (train/oos и т.п.);
	/// - базовый AUC;
	/// - размер выборки (rows/pos/neg).
	/// Источник данных — FeatureImportanceSnapshots, которые уже собираются PFI-кодом.
	/// </summary>
	public static class ModelStatsReportBuilder
		{
		public static ReportDocument BuildFromSnapshots (
			IReadOnlyList<FeatureImportanceSnapshot> snapshots,
			string? explicitTitle = null )
			{
			if (snapshots == null) throw new ArgumentNullException (nameof (snapshots));

			var now = DateTime.UtcNow;

			var doc = new ReportDocument
				{
				Id = $"backtest-model-stats-{now:yyyyMMdd_HHmmss}",
				Kind = "backtest_model_stats",
				Title = explicitTitle ?? "Статистика моделей (PFI / AUC)",
				GeneratedAtUtc = now,
				KeyValueSections = new List<KeyValueSection> (),
				TableSections = new List<TableSection> (),
				TextSections = new List<TextSection> ()
				};

			// Одна таблица: overview по моделям.
			var table = new TableSection
				{
				Title = "Models overview"
				};

			table.Columns.AddRange (new[]
			{
				"Tag",
				"BaselineAuc",
				"Rows",
				"Pos",
				"Neg"
			});

			foreach (var s in snapshots)
				{
				var stats = s.Stats ?? Array.Empty<FeatureStats> ();

				int pos = 0;
				int neg = 0;
				int rows = 0;

				if (stats.Count > 0)
					{
					// CountPos/CountNeg одинаковы для всех фич одной модели,
					// поэтому берём из первой.
					var first = stats[0];
					pos = first.CountPos;
					neg = first.CountNeg;
					rows = pos + neg;
					}

				table.Rows.Add (new List<string>
				{
					s.Tag,
					s.BaselineAuc.ToString("0.000"),
					rows.ToString(),
					pos.ToString(),
					neg.ToString()
				});
				}

			doc.TableSections.Add (table);

			return doc;
			}
		}
	}
