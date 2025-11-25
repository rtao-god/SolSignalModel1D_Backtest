namespace SolSignalModel1D_Backtest.Core.Analytics.Reporting
	{
	/// <summary>
	/// Описание одной колонки таблицы для произвольного типа строки TRow.
	/// </summary>
	public sealed class MetricColumnDefinition<TRow>
		{
		/// <summary>
		/// Внутренний ключ колонки (для фронта, тестов и т.п.).
		/// Должен быть уникальным в рамках одной таблицы.
		/// </summary>
		public string Key { get; }

		/// <summary>
		/// Заголовок колонки в простом режиме (Simple).
		/// Например: "Профит, %".
		/// </summary>
		public string SimpleTitle { get; }

		/// <summary>
		/// Заголовок колонки в техническом режиме (Technical).
		/// Например: "TotalPnlPct (rel)".
		/// </summary>
		public string TechnicalTitle { get; }

		/// <summary>
		/// Минимальный уровень детализации, при котором колонка видна.
		/// Simple = видна везде, Technical = только в технарском режиме.
		/// </summary>
		public TableDetailLevel MinLevel { get; }

		/// <summary>
		/// Функция, которая из строки данных (TRow) делает строковое значение для ячейки.
		/// Здесь же можно сделать форматирование (проценты, округление и т.п.).
		/// </summary>
		public Func<TRow, string> ValueSelector { get; }

		public MetricColumnDefinition (
			string key,
			string simpleTitle,
			string technicalTitle,
			TableDetailLevel minLevel,
			Func<TRow, string> valueSelector )
			{
			Key = key ?? throw new ArgumentNullException (nameof (key));
			SimpleTitle = simpleTitle ?? throw new ArgumentNullException (nameof (simpleTitle));
			TechnicalTitle = technicalTitle ?? throw new ArgumentNullException (nameof (technicalTitle));
			MinLevel = minLevel;
			ValueSelector = valueSelector ?? throw new ArgumentNullException (nameof (valueSelector));
			}
		}
	}
