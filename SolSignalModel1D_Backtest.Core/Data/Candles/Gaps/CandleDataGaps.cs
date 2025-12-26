using System;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// Описание "известной" дыры в данных свечей: что ожидали и с какого бара ряд реально продолжился.
	/// </summary>
	public readonly struct KnownCandleGap
		{
		public KnownCandleGap (
			string symbol,
			string interval,
			DateTime expectedStartUtc,
			DateTime actualStartUtc )
			{
			Symbol = symbol;
			Interval = interval;
			ExpectedStartUtc = expectedStartUtc;
			ActualStartUtc = actualStartUtc;
			}

		public string Symbol { get; }
		public string Interval { get; }
		public DateTime ExpectedStartUtc { get; }
		public DateTime ActualStartUtc { get; }
		}

	/// <summary>
	/// Централизованный список известных дыр по таймфреймам.
	/// Если всплывут новые, лучше добавлять сюда (или в отдельные partial-файлы),
	/// а не раскидывать по коду апдейтеров.
	/// </summary>
	public static partial class CandleDataGaps
		{
		/// <summary>
		/// Известные дыры для 1h-свечей (Binance).
		/// Сейчас пусто — заполнится после gap-scan по 1h.
		/// </summary>
		public static readonly KnownCandleGap[] Known1hGaps =
			{
			};

		/// <summary>
		/// Известные дыры для 6h-свечей (Binance).
		/// Сейчас пусто — заполнится после gap-scan по 6h.
		/// </summary>
		public static readonly KnownCandleGap[] Known6hGaps =
			{
			};

		/// <summary>
		/// Ищет "известную" дыру по (symbol, interval, expectedStartUtc, actualStartUtc).
		/// Это используется апдейтером, чтобы:
		/// - логировать проблему;
		/// - продолжать работу без synthetic fill;
		/// - не маскировать неизвестные дырки (на них всё равно падаем).
		/// </summary>
		public static bool TryMatchKnownGap (
			string symbol,
			string interval,
			DateTime expectedStartUtc,
			DateTime actualStartUtc,
			out KnownCandleGap gap )
			{
			if (string.IsNullOrWhiteSpace (symbol))
				{
				gap = default;
				return false;
				}

			symbol = symbol.Trim ().ToUpperInvariant ();

			var list = interval switch
				{
					"1m" => Known1mGaps,
					"1h" => Known1hGaps,
					"6h" => Known6hGaps,
					_ => Array.Empty<KnownCandleGap> ()
					};

			for (int i = 0; i < list.Length; i++)
				{
				var g = list[i];

				// Сравнение строгое: gap-scan даёт точные expected/actual.
				if (!string.Equals (g.Interval, interval, StringComparison.Ordinal))
					continue;

				if (!string.Equals (g.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
					continue;

				if (g.ExpectedStartUtc != expectedStartUtc)
					continue;

				if (g.ActualStartUtc != actualStartUtc)
					continue;

				gap = g;
				return true;
				}

			gap = default;
			return false;
			}
		}
	}
