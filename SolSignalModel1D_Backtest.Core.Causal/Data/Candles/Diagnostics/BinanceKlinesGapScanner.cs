using System.Text.Json;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Diagnostics
	{
	/// <summary>
	/// Диагностический сканер: проходится по историческим klines Binance
	/// и логирует все разрывы по времени для заданного интервала.
	/// Ничего не пишет на диск, только консоль.
	/// </summary>
	public static class BinanceKlinesGapScanner
		{
		/// <summary>
		/// Полный проход от fromUtc до toUtc по klines Binance
		/// с логированием всех временных дыр.
		/// </summary>
		public static async Task ScanGapsAsync (
			HttpClient http,
			string symbol,
			string interval,
			TimeSpan tf,
			DateTime fromUtc,
			DateTime toUtc )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));
			if (string.IsNullOrWhiteSpace (symbol)) throw new ArgumentException ("symbol пустой", nameof (symbol));
			if (toUtc <= fromUtc) throw new ArgumentException ("toUtc <= fromUtc", nameof (toUtc));

			symbol = symbol.Trim ().ToUpperInvariant ();
			string symbolEsc = Uri.EscapeDataString (symbol);

			long startMs = new DateTimeOffset (fromUtc).ToUnixTimeMilliseconds ();
			long endMs = new DateTimeOffset (toUtc).ToUnixTimeMilliseconds ();

			const int limit = 1000;

			long cursor = startMs;
			DateTime? prev = null;
			long totalCandles = 0;
			long totalGaps = 0;

			Console.WriteLine (
				$"[gap-scan] {symbol} {interval}: start scan [{fromUtc:O}..{toUtc:O}], tf={tf}.");

			while (cursor < endMs)
				{
				string url =
					$"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval={interval}&limit={limit}&startTime={cursor}&endTime={endMs}";

				using var resp = await http.GetAsync (url);
				if (!resp.IsSuccessStatusCode)
					{
					Console.WriteLine (
						$"[gap-scan] HTTP {(int) resp.StatusCode} при загрузке {symbol} {interval}, url={url}");
					resp.EnsureSuccessStatusCode ();
					}

				await using var s = await resp.Content.ReadAsStreamAsync ();
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

				if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength () == 0)
					{
					// Binance вернул пустую страницу — история на этом закончилась.
					break;
					}

				long maxTs = 0;

				foreach (var el in root.EnumerateArray ())
					{
					// klines: [ openTime, open, high, low, close, ... ]
					long openTime = el[0].GetInt64 ();
					var dt = DateTimeOffset.FromUnixTimeMilliseconds (openTime).UtcDateTime;

					if (prev.HasValue)
						{
						var expected = prev.Value + tf;
						if (dt != expected)
							{
							var gap = dt - expected;
							var gapMinutes = gap.TotalMinutes;

							Console.WriteLine (
								$"[gap-scan] {symbol} {interval}: GAP prev={prev:O}, " +
								$"expected={expected:O}, actual={dt:O}, gapMinutes={gapMinutes:F0}");

							totalGaps++;
							}
						}

					prev = dt;
					totalCandles++;

					if (openTime > maxTs)
						maxTs = openTime;
					}

				if (maxTs == 0)
					{
					var cursorDt = DateTimeOffset.FromUnixTimeMilliseconds (cursor).UtcDateTime;
					var endDt = DateTimeOffset.FromUnixTimeMilliseconds (endMs).UtcDateTime;

					var msg =
						$"[gap-scan] {symbol} {interval}: invalid klines payload, maxTs=0 " +
						$"for page [{cursorDt:O}..{endDt:O}], url={url}";
					Console.WriteLine (msg);
					throw new InvalidOperationException (msg);
					}

				cursor = maxTs + 1;
				}

			Console.WriteLine (
				$"[gap-scan] {symbol} {interval}: completed. candles={totalCandles}, " +
				$"gaps={totalGaps}, range=[{fromUtc:O}..{toUtc:O}]");
			}
		}
	}
