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
	/// Если всплывут новые, лучше добавлять сюда, а не раскидывать по коду.
	/// </summary>
	public static class CandleDataGaps
		{
		/// <summary>
		/// Известные дыры для 1m-свечей (Binance).
		/// </summary>
		public static readonly KnownCandleGap[] Known1mGaps =
			{
			// SOLUSDT: gap 2021-08-13 02:00..06:30 (270 минут).
			new KnownCandleGap (
				symbol: "SOLUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2021, 8, 13, 2, 0, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2021, 8, 13, 6, 30, 0, DateTimeKind.Utc)),

			// BTCUSDT: та же самая дыра.
			new KnownCandleGap (
				symbol: "BTCUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2021, 8, 13, 2, 0, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2021, 8, 13, 6, 30, 0, DateTimeKind.Utc))
			};
		}
	}
