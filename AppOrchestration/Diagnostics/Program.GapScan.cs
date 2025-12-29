using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Diagnostics;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static async Task RunBinance1mGapScanAsync ()
			{
			var fromUtc = new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);
			var toUtc = DateTime.UtcNow;

			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes (5) };

			// PAXG 1m не используется — намеренно пропускаем.
			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "SOLUSDT",
				interval: "1m",
				tf: TimeSpan.FromMinutes (1),
				fromUtc: fromUtc,
				toUtc: toUtc);

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "BTCUSDT",
				interval: "1m",
				tf: TimeSpan.FromMinutes (1),
				fromUtc: fromUtc,
				toUtc: toUtc);
			}

		private static async Task RunBinance1hGapScanAsync ()
			{
			var fromUtc = new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);
			var toUtc = DateTime.UtcNow;

			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes (5) };

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "SOLUSDT",
				interval: "1h",
				tf: TimeSpan.FromHours (1),
				fromUtc: fromUtc,
				toUtc: toUtc);

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "BTCUSDT",
				interval: "1h",
				tf: TimeSpan.FromHours (1),
				fromUtc: fromUtc,
				toUtc: toUtc);

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "PAXGUSDT",
				interval: "1h",
				tf: TimeSpan.FromHours (1),
				fromUtc: fromUtc,
				toUtc: toUtc);
			}

		private static async Task RunBinance6hGapScanAsync ()
			{
			var fromUtc = new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);
			var toUtc = DateTime.UtcNow;

			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes (5) };

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "SOLUSDT",
				interval: "6h",
				tf: TimeSpan.FromHours (6),
				fromUtc: fromUtc,
				toUtc: toUtc);

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "BTCUSDT",
				interval: "6h",
				tf: TimeSpan.FromHours (6),
				fromUtc: fromUtc,
				toUtc: toUtc);

			await BinanceKlinesGapScanner.ScanGapsAsync (
				http,
				symbol: "PAXGUSDT",
				interval: "6h",
				tf: TimeSpan.FromHours (6),
				fromUtc: fromUtc,
				toUtc: toUtc);
			}
		}
	}
