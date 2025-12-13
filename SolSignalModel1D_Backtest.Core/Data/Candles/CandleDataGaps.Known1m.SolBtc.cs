using System;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public static partial class CandleDataGaps
		{
		/// <summary>
		/// Известные дыры для 1m-свечей (Binance) по SOL/BTC.
		/// Вынесено отдельно, чтобы не засорять основной список.
		/// PAXG 1m здесь намеренно отсутствует (1m не используется).
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
				actualStartUtc:   new DateTime (2021, 8, 13, 6, 30, 0, DateTimeKind.Utc)),

			// SOLUSDT: gap 2021-09-29 07:00..09:00 (120 минут).
			new KnownCandleGap (
				symbol: "SOLUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2021, 9, 29, 7, 0, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2021, 9, 29, 9, 0, 0, DateTimeKind.Utc)),

			// BTCUSDT: та же самая дыра.
			new KnownCandleGap (
				symbol: "BTCUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2021, 9, 29, 7, 0, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2021, 9, 29, 9, 0, 0, DateTimeKind.Utc)),

			// SOLUSDT: gap 2023-03-24 12:40..14:00 (80 минут).
			new KnownCandleGap (
				symbol: "SOLUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2023, 3, 24, 12, 40, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2023, 3, 24, 14, 0, 0, DateTimeKind.Utc)),

			// BTCUSDT: та же самая дыра.
			new KnownCandleGap (
				symbol: "BTCUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2023, 3, 24, 12, 40, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2023, 3, 24, 14, 0, 0, DateTimeKind.Utc))
			};
		}																
	}