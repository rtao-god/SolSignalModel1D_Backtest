namespace SolSignalModel1D_Backtest.Reports.Reporting
	{
	/// <summary>
	/// Описание таблицы метрик для типа строки TRow.
	/// </summary>
	public sealed class MetricTableDefinition<TRow>
		{
		/// <summary>
		/// Уникальный ключ таблицы (например, "backtest_policies").
		/// </summary>
		public string TableKey { get; }

		/// <summary>
		/// Заголовок таблицы для пользователя.
		/// </summary>
		public string Title { get; }

		/// <summary>
		/// Список колонок (с уровнями детализации и селекторами значений).
		/// </summary>
		public IReadOnlyList<MetricColumnDefinition<TRow>> Columns { get; }

		public MetricTableDefinition (
			string tableKey,
			string title,
			IReadOnlyList<MetricColumnDefinition<TRow>> columns )
			{
			TableKey = tableKey ?? throw new ArgumentNullException (nameof (tableKey));
			Title = title ?? throw new ArgumentNullException (nameof (title));
			Columns = columns ?? throw new ArgumentNullException (nameof (columns));
			}
		}
	}
