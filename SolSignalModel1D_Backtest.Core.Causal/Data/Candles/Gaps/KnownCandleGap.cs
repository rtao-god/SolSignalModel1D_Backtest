namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps
	{
	public static partial class CandleDataGaps
		{
		/// <summary>
		/// Известные дыры для 1m-свечей (Binance).
		///
		/// Правило:
		/// - сюда попадают ТОЛЬКО "stable" дыры, подтверждённые BinanceGapDiscovery (seen in ALL passes);
		/// - локальные проблемы кеша (например, усечённый NDJSON) сюда НЕ добавляются.
		/// </summary>
		public static readonly KnownCandleGap[] Known1mGaps =
			{
			// === SOLUSDT (stable; confirmed by 3/3 passes) ===

			new KnownCandleGap (
				symbol: "SOLUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2021, 8, 13, 2, 0, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2021, 8, 13, 6, 30, 0, DateTimeKind.Utc)),

			new KnownCandleGap (
				symbol: "SOLUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2021, 9, 29, 7, 0, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2021, 9, 29, 9, 0, 0, DateTimeKind.Utc)),

			new KnownCandleGap (
				symbol: "SOLUSDT",
				interval: "1m",
				expectedStartUtc: new DateTime (2023, 3, 24, 12, 40, 0, DateTimeKind.Utc),
				actualStartUtc:   new DateTime (2023, 3, 24, 14, 0, 0, DateTimeKind.Utc)),
			};
		}
	}
