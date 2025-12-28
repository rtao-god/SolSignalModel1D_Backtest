using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.Utils
	{
	/// <summary>
	/// Простая таблица с поддержкой цвета для строк.
	/// </summary>
	public sealed class TextTable
		{
		private sealed class Row
			{
			public string[] Cells { get; set; } = Array.Empty<string> ();
			public ConsoleColor? Color { get; set; }
			public bool IsHeader { get; set; }
			}

		private readonly List<Row> _rows = new List<Row> ();

		public void AddRow ( params string[] cells )
			{
			_rows.Add (new Row { Cells = cells });
			}

		public void AddHeader ( params string[] cells )
			{
			_rows.Add (new Row { Cells = cells, IsHeader = true });
			}

		public void AddColoredRow ( ConsoleColor color, params string[] cells )
			{
			_rows.Add (new Row { Cells = cells, Color = color });
			}

		public void WriteToConsole ()
			{
			if (_rows.Count == 0)
				return;

			int cols = _rows.Max (r => r.Cells.Length);
			var widths = new int[cols];

			foreach (var row in _rows)
				{
				for (int i = 0; i < row.Cells.Length; i++)
					{
					int len = row.Cells[i]?.Length ?? 0;
					if (len > widths[i]) widths[i] = len;
					}
				}

			foreach (var row in _rows)
				{
				ConsoleColor? toSet = row.Color;
				if (row.IsHeader)
					toSet = ConsoleStyler.HeaderColor;

				var prev = Console.ForegroundColor;
				if (toSet.HasValue)
					Console.ForegroundColor = toSet.Value;

				for (int i = 0; i < cols; i++)
					{
					string cell = i < row.Cells.Length ? row.Cells[i] ?? "" : "";
					Console.Write (cell.PadRight (widths[i] + 2));
					}

				Console.WriteLine ();

				if (row.IsHeader)
					{
					// подчёркивание
					for (int i = 0; i < cols; i++)
						{
						string underline = new string ('-', widths[i]);
						Console.Write (underline.PadRight (widths[i] + 2));
						}
					Console.WriteLine ();
					}

				if (toSet.HasValue)
					Console.ForegroundColor = prev;
				}
			}
		}
	}
