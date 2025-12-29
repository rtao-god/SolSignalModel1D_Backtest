namespace SolSignalModel1D_Backtest.Core.Causal.Utils
	{
	/// <summary>
	/// Базовые стили для консоли: цвета, “жирный” (ANSI), заголовки.
	/// </summary>
	public static class ConsoleStyler
		{
		// если хочешь отключить ANSI — поставь false
		public static bool UseAnsi = true;

		private const string BoldOn = "\u001b[1m";
		private const string BoldOff = "\u001b[22m";

		public static void WriteHeader ( string text )
			{
			WriteLineBold (text);
			}

		public static void WriteLineBold ( string text )
			{
			if (UseAnsi)
				{
				Console.WriteLine ($"{BoldOn}{text}{BoldOff}");
				}
			else
				{
				Console.WriteLine (text.ToUpperInvariant ());
				}
			}

		public static string Bold ( string text )
			{
			if (UseAnsi)
				return $"{BoldOn}{text}{BoldOff}";
			return text.ToUpperInvariant ();
			}

		public static void WithColor ( ConsoleColor color, Action action )
			{
			var prev = Console.ForegroundColor;
			Console.ForegroundColor = color;
			action ();
			Console.ForegroundColor = prev;
			}

		public static string Colorize ( string text, ConsoleColor color )
			{
			// В строку цвет корректно не засунуть без ANSI, поэтому вернём как есть.
			// Мы будем красить строку целиком при выводе.
			return text;
			}

		public static ConsoleColor GoodColor => ConsoleColor.Green;
		public static ConsoleColor BadColor => ConsoleColor.Red;
		public static ConsoleColor HeaderColor => ConsoleColor.Cyan;
		public static ConsoleColor DimColor => ConsoleColor.DarkGray;
		}
	}
