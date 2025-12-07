using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Reporting
	{
	/// <summary>
	/// Универсальный билдер, который из описания таблицы и набора строк
	/// строит TableSection для конкретного уровня детализации.
	/// </summary>
	public static class MetricTableBuilder
		{
		/// <summary>
		/// Строит TableSection (табличный раздел отчёта) для заданного уровня детализации.
		/// </summary>
		public static TableSection BuildTable<TRow> (
			MetricTableDefinition<TRow> definition,
			IEnumerable<TRow> rows,
			TableDetailLevel level,
			string? explicitTitle = null )
			{
			if (definition == null) throw new ArgumentNullException (nameof (definition));
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			// 1. Выбираем колонки, которые видимы на данном уровне.
			var activeColumns = definition.Columns
				.Where (c => c.MinLevel <= level)
				.ToList ();

			// 2. Заголовки с учётом режима.
			var columnTitles = activeColumns
				.Select (c => level == TableDetailLevel.Simple ? c.SimpleTitle : c.TechnicalTitle)
				.ToList ();

			// 3. Строим строки.
			var rowList = new List<List<string>> ();

			foreach (var row in rows)
				{
				var cells = new List<string> (activeColumns.Count);

				foreach (var col in activeColumns)
					{
					// Здесь никаких пересчётов — только вызов ValueSelector.
					var value = col.ValueSelector (row) ?? string.Empty;
					cells.Add (value);
					}

				rowList.Add (cells);
				}

			return new TableSection
				{
				Title = explicitTitle ?? definition.Title,
				Columns = columnTitles,
				Rows = rowList
				};
			}
		}
	}
