using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Reports.Model
	{
	/// <summary>
	/// Универсальный отчёт:
	/// - Kind = тип отчёта ("current_prediction", "backtest_summary" и т.п.).
	/// - Набор секций: key-value, таблицы, текст.
	/// </summary>
	public sealed class ReportDocument
		{
		public string Id { get; set; } = string.Empty;
		public string Kind { get; set; } = string.Empty;
		public string Title { get; set; } = string.Empty;
		public DateTime GeneratedAtUtc { get; set; }

		public List<KeyValueSection> KeyValueSections { get; set; } = new ();
		public List<TableSection> TableSections { get; set; } = new ();
		public List<TextSection> TextSections { get; set; } = new ();
		}

	/// <summary>
	/// Раздел вида "ключ-значение".
	/// </summary>
	public sealed class KeyValueSection
		{
		public string Title { get; set; } = string.Empty;
		public List<KeyValueItem> Items { get; set; } = new ();
		}

	public sealed class KeyValueItem
		{
		public string Key { get; set; } = string.Empty;
		public string Value { get; set; } = string.Empty;
		}

	/// <summary>
	/// Табличный раздел.
	/// </summary>
	public sealed class TableSection
		{
		public string Title { get; set; } = string.Empty;
		public List<string> Columns { get; set; } = new ();
		public List<List<string>> Rows { get; set; } = new ();
		}

	/// <summary>
	/// Свободный текстовый раздел (описания, комментарии).
	/// </summary>
	public sealed class TextSection
		{
		public string Title { get; set; } = string.Empty;
		public string Text { get; set; } = string.Empty;
		}
	}
