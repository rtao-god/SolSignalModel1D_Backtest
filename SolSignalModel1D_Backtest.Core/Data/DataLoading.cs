using System.Globalization;
using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public static class DataLoading
		{
		private static readonly Dictionary<string, double> DxyWeights = new ()
			{
			["EUR"] = 57.6,
			["JPY"] = 13.6,
			["GBP"] = 11.9,
			["CAD"] = 9.1,
			["SEK"] = 4.2,
			["CHF"] = 3.6
			};

		// ===== 1m =====
		public static async Task<List<Candle1m>> GetBinance1m (
			HttpClient http,
			string symbol,
			int max )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));
			if (max <= 0) throw new ArgumentOutOfRangeException (nameof (max), "max должен быть > 0");

			symbol = (symbol ?? string.Empty).Trim ().ToUpperInvariant ();
			int ws = symbol.IndexOfAny (new[] { ' ', '\t', '\r', '\n' });
			if (ws >= 0)
				symbol = symbol.Substring (0, ws);

			string symbolEsc = Uri.EscapeDataString (symbol);

			const int chunk = 1000;
			var all = new List<Candle1m> (max);
			long? endTimeMs = null;

			try
				{
				while (all.Count < max)
					{
					int need = Math.Min (chunk, max - all.Count);
					string url =
						$"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval=1m&limit={need}";
					if (endTimeMs.HasValue)
						url += $"&endTime={endTimeMs.Value}";

					using var resp = await http.GetAsync (url);
					if (!resp.IsSuccessStatusCode)
						{
						Console.WriteLine ($"[binance-1m] HTTP {(int) resp.StatusCode} при загрузке {symbol}, url={url}");
						resp.EnsureSuccessStatusCode ();
						}

					await using var s = await resp.Content.ReadAsStreamAsync ();
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

					if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength () == 0)
						break;

					long earliestOpenMs = long.MaxValue;

					foreach (var el in root.EnumerateArray ())
						{
						long openTime = el[0].GetInt64 ();
						double open = double.Parse (el[1].GetString ()!, CultureInfo.InvariantCulture);
						double high = double.Parse (el[2].GetString ()!, CultureInfo.InvariantCulture);
						double low = double.Parse (el[3].GetString ()!, CultureInfo.InvariantCulture);
						double close = double.Parse (el[4].GetString ()!, CultureInfo.InvariantCulture);

						DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds (openTime).UtcDateTime;
						all.Insert (0, new Candle1m
							{
							OpenTimeUtc = dt,
							Open = open,
							High = high,
							Low = low,
							Close = close
							});

						if (openTime < earliestOpenMs)
							earliestOpenMs = openTime;
						}

					const long oneMinuteMs = 60L * 1000;
					endTimeMs = earliestOpenMs - oneMinuteMs;

					if (all.Count >= max)
						break;
					}

				all.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
				if (all.Count > max)
					all = all.GetRange (all.Count - max, max);

				return all;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[binance-1m] ошибка при загрузке {symbol}: {ex}");
				throw;
				}
			}

		// ===== 6h =====
		public static async Task<List<Candle6h>> GetBinance6h (
			HttpClient http,
			string symbol,
			int max )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));
			if (max <= 0) throw new ArgumentOutOfRangeException (nameof (max), "max должен быть > 0");

			symbol = (symbol ?? string.Empty).Trim ().ToUpperInvariant ();
			int ws = symbol.IndexOfAny (new[] { ' ', '\t', '\r', '\n' });
			if (ws >= 0)
				symbol = symbol.Substring (0, ws);

			string symbolEsc = Uri.EscapeDataString (symbol);

			const int chunk = 1000;
			var all = new List<Candle6h> (max);
			long? endTimeMs = null;

			try
				{
				while (all.Count < max)
					{
					int need = Math.Min (chunk, max - all.Count);

					string url =
						$"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval=6h&limit={need}";
					if (endTimeMs.HasValue)
						url += $"&endTime={endTimeMs.Value}";

					using var resp = await http.GetAsync (url);
					if (!resp.IsSuccessStatusCode)
						{
						Console.WriteLine ($"[binance-6h] HTTP {(int) resp.StatusCode} при загрузке {symbol}, url={url}");
						resp.EnsureSuccessStatusCode ();
						}

					await using var s = await resp.Content.ReadAsStreamAsync ();
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

					if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength () == 0)
						break;

					var batch = new List<Candle6h> (root.GetArrayLength ());
					long earliestOpenMs = long.MaxValue;

					foreach (var el in root.EnumerateArray ())
						{
						long openTime = el[0].GetInt64 ();
						double open = double.Parse (el[1].GetString ()!, CultureInfo.InvariantCulture);
						double high = double.Parse (el[2].GetString ()!, CultureInfo.InvariantCulture);
						double low = double.Parse (el[3].GetString ()!, CultureInfo.InvariantCulture);
						double close = double.Parse (el[4].GetString ()!, CultureInfo.InvariantCulture);

						DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds (openTime).UtcDateTime;
						batch.Add (new Candle6h
							{
							OpenTimeUtc = dt,
							Open = open,
							High = high,
							Low = low,
							Close = close
							});

						if (openTime < earliestOpenMs)
							earliestOpenMs = openTime;
						}

					if (batch.Count == 0)
						break;

					batch.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
					all.InsertRange (0, batch);

					const long sixHoursMs = 6L * 60 * 60 * 1000;
					endTimeMs = earliestOpenMs - sixHoursMs;

					if (batch.Count < need)
						break;
					}

				all.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
				if (all.Count > max)
					all = all.GetRange (all.Count - max, max);

				return all;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[binance-6h] ошибка при загрузке {symbol}: {ex}");
				throw;
				}
			}

		// ===== 1h =====
		public static async Task<List<Candle1h>> GetBinance1h (
			HttpClient http,
			string symbol,
			int max )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));
			if (max <= 0) throw new ArgumentOutOfRangeException (nameof (max), "max должен быть > 0");

			symbol = (symbol ?? string.Empty).Trim ().ToUpperInvariant ();
			int ws = symbol.IndexOfAny (new[] { ' ', '\t', '\r', '\n' });
			if (ws >= 0)
				symbol = symbol.Substring (0, ws);

			string symbolEsc = Uri.EscapeDataString (symbol);

			const int chunk = 1000;
			var all = new List<Candle1h> (max);
			long? endTimeMs = null;

			try
				{
				while (all.Count < max)
					{
					int need = Math.Min (chunk, max - all.Count);

					string url =
						$"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval=1h&limit={need}";
					if (endTimeMs.HasValue)
						url += $"&endTime={endTimeMs.Value}";

					using var resp = await http.GetAsync (url);
					if (!resp.IsSuccessStatusCode)
						{
						Console.WriteLine ($"[binance-1h] HTTP {(int) resp.StatusCode} при загрузке {symbol}, url={url}");
						resp.EnsureSuccessStatusCode ();
						}

					await using var s = await resp.Content.ReadAsStreamAsync ();
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

					if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength () == 0)
						break;

					long earliestOpenMs = long.MaxValue;

					foreach (var el in root.EnumerateArray ())
						{
						long openTime = el[0].GetInt64 ();
						double open = double.Parse (el[1].GetString ()!, CultureInfo.InvariantCulture);
						double high = double.Parse (el[2].GetString ()!, CultureInfo.InvariantCulture);
						double low = double.Parse (el[3].GetString ()!, CultureInfo.InvariantCulture);
						double close = double.Parse (el[4].GetString ()!, CultureInfo.InvariantCulture);

						DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds (openTime).UtcDateTime;
						all.Insert (0, new Candle1h
							{
							OpenTimeUtc = dt,
							Open = open,
							High = high,
							Low = low,
							Close = close
							});

						if (openTime < earliestOpenMs)
							earliestOpenMs = openTime;
						}

					const long oneHourMs = 60L * 60 * 1000;
					endTimeMs = earliestOpenMs - oneHourMs;

					if (all.Count >= max)
						break;
					}

				all.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
				if (all.Count > max)
					all = all.GetRange (all.Count - max, max);

				return all;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[binance-1h] ошибка при загрузке {symbol}: {ex}");
				throw;
				}
			}

		// ===== FNG / DXY / extra =====

		public static async Task<Dictionary<DateTime, double>> GetFngHistory ( HttpClient http )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));

			var dict = new Dictionary<DateTime, double> ();

			try
				{
				string url = "https://api.alternative.me/fng/?limit=1000";
				using var resp = await http.GetAsync (url);
				if (!resp.IsSuccessStatusCode)
					{
					Console.WriteLine ($"[fng] HTTP {(int) resp.StatusCode} при загрузке FNG, url={url}");
					resp.EnsureSuccessStatusCode ();
					}

				await using var s = await resp.Content.ReadAsStreamAsync ();
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

				if (root.TryGetProperty ("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
					{
					foreach (var el in arr.EnumerateArray ())
						{
						if (!el.TryGetProperty ("timestamp", out var tsEl)) continue;
						if (!long.TryParse (tsEl.GetString (), out long ts)) continue;
						DateTime d = DateTimeOffset.FromUnixTimeSeconds (ts).UtcDateTime.Causal.DateUtc;
						if (el.TryGetProperty ("value", out var vEl) && int.TryParse (vEl.GetString (), out int v))
							dict[d] = v;
						}
					}

				return dict;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[fng] ошибка при загрузке FNG: {ex}");
				throw;
				}
			}

		public static async Task<Dictionary<DateTime, double>> GetDxySeries ( HttpClient http, DateTime start, DateTime end )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));
			if (end < start)
				throw new ArgumentException ("end < start для диапазона DXY", nameof (end));

			var dict = new Dictionary<DateTime, double> ();

			try
				{
				string to = "EUR,JPY,GBP,CAD,SEK,CHF";
				string url = $"https://api.frankfurter.app/{start:yyyy-MM-dd}..{end:yyyy-MM-dd}?from=USD&to={to}";
				using var resp = await http.GetAsync (url);
				if (!resp.IsSuccessStatusCode)
					{
					Console.WriteLine ($"[dxy] HTTP {(int) resp.StatusCode} при загрузке DXY, url={url}");
					resp.EnsureSuccessStatusCode ();
					}

				await using var s = await resp.Content.ReadAsStreamAsync ();
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

				if (root.TryGetProperty ("rates", out var rates))
					{
					foreach (var day in rates.EnumerateObject ())
						{
						if (!DateTime.TryParse (day.Name, out var d))
							continue;

						double idx = IndexFromRates (day.Value, out int used);
						DateTime d = DateTimeOffset.FromUnixTimeSeconds (ts).UtcDateTime.Causal.DateUtc;
						if (!double.IsNaN (idx) && used >= 4)
							dict[d.Causal.DateUtc] = idx;
						}
					}

				return dict;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[dxy] ошибка при загрузке DXY: {ex}");
				throw;
				}
			}

		private static double IndexFromRates ( JsonElement rates, out int used )
			{
			double sumW = 0, acc = 0;
			used = 0;
			foreach (var kv in DxyWeights)
				{
				if (!rates.TryGetProperty (kv.Key, out var re) || re.ValueKind != JsonValueKind.Number)
					continue;
				double r = re.GetDouble ();
				if (r <= 0) continue;
				double w = kv.Value / 100.0;
				acc += w * Math.Log (r);
				sumW += w;
				used++;
				}
			if (used == 0 || sumW == 0) return double.NaN;
			return Math.Exp (acc / sumW);
			}

		public static Dictionary<DateTime, (double Funding, double OI)>? TryLoadExtraDaily ( string path )
			{
			if (string.IsNullOrWhiteSpace (path))
				throw new ArgumentException ("path пустой", nameof (path));

			if (!File.Exists (path))
				return null;

			try
				{
				var txt = File.ReadAllText (path);
				var arr = JsonSerializer.Deserialize<List<ExtraPoint>> (txt);
				if (arr == null)
					throw new InvalidOperationException ($"[extra] JSON десериализован в null, path='{path}'");

				var dict = new Dictionary<DateTime, (double, double)> ();
				foreach (var e in arr)
					dict[e.Date.ToCausalDateUtc ()] = (e.Funding, e.OI);

				Console.WriteLine ($"[extra] загружено {dict.Count} строк доп. данных из '{path}'");
				return dict;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[extra] ошибка при загрузке доп. данных из '{path}': {ex}");
				throw;
				}
			}

		internal struct ExtraPoint
			{
			public DateTime Date { get; set; }
			public double Funding { get; set; }
			public double OI { get; set; }
			}

		// ===== диапазонный загрузчик =====
		public static async Task<List<(DateTime openUtc, double open, double high, double low,     double close)>> GetBinanceKlinesRange (
			HttpClient http,
			string symbol,
			string interval,
			DateTime fromUtc,
			DateTime toUtc )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));

			var intervalLen = TryGetBinanceIntervalLength (interval);

			if (toUtc <= fromUtc)
				{
				if (intervalLen.HasValue)
					{
					var diff = fromUtc - toUtc; // >= 0 при to<=from

					// Маленький "отрицательный" диапазон (ещё не началась следующая свеча) — нормально,
					// просто ничего не загружаем.
					if (diff <= intervalLen.Value + TimeSpan.FromSeconds (5))
						{
						return new List<(DateTime openUtc, double open, double high, double low, double close)> (0);
						}
					}

				// Реальная аномалия — toUtc сильно меньше fromUtc.
				throw new ArgumentException ("toUtc < fromUtc для диапазона klines", nameof (toUtc));
				}

			symbol = (symbol ?? string.Empty).Trim ().ToUpperInvariant ();
			string symbolEsc = Uri.EscapeDataString (symbol);

			long startMs = new DateTimeOffset (fromUtc).ToUnixTimeMilliseconds ();
			long endMs = new DateTimeOffset (toUtc).ToUnixTimeMilliseconds ();

			const int limit = 1000;

			var result = new List<(DateTime openUtc, double open, double high, double low, double close)> (1024);

			long cursor = startMs;

			while (cursor < endMs)
				{
				string url =
					$"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval={interval}&limit={limit}&startTime={cursor}&endTime={endMs}";

				using var resp = await http.GetAsync (url);
				if (!resp.IsSuccessStatusCode)
					{
					Console.WriteLine (
						$"[binance-range] HTTP {(int) resp.StatusCode} при загрузке {symbol} {interval}, url={url}");
					resp.EnsureSuccessStatusCode ();
					}

				await using var s = await resp.Content.ReadAsStreamAsync ();
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);

				if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength () == 0)
					break;

				long maxTs = 0;

				foreach (var el in root.EnumerateArray ())
					{
					long openTime = el[0].GetInt64 ();
					double open = double.Parse (el[1].GetString ()!, CultureInfo.InvariantCulture);
					double high = double.Parse (el[2].GetString ()!, CultureInfo.InvariantCulture);
					double low = double.Parse (el[3].GetString ()!, CultureInfo.InvariantCulture);
					double close = double.Parse (el[4].GetString ()!, CultureInfo.InvariantCulture);

					var dt = DateTimeOffset.FromUnixTimeMilliseconds (openTime).UtcDateTime;
					result.Add ((dt, open, high, low, close));

					if (openTime > maxTs)
						maxTs = openTime;
					}

				if (maxTs == 0)
					{
					// Ненормальная ситуация: Binance вернул непустой массив,
					// но ни одной валидной метки времени мы не увидели.
					var cursorDt = DateTimeOffset.FromUnixTimeMilliseconds (cursor).UtcDateTime;
					var endDt = DateTimeOffset.FromUnixTimeMilliseconds (endMs).UtcDateTime;

					var msg =
						$"[binance-range] {symbol} {interval}: invalid klines payload, maxTs=0 " +
						$"for page [{cursorDt:O}..{endDt:O}], url={url}";

					Console.WriteLine (msg);
					throw new InvalidOperationException (msg);
					}

				cursor = maxTs + 1;
				}

			result.Sort (( a, b ) => a.openUtc.CompareTo (b.openUtc));
			return result;
			}

		// helper 
		private static TimeSpan? TryGetBinanceIntervalLength ( string interval )
			{
			return interval switch
				{
					"1m" => TimeSpan.FromMinutes (1),
					"3m" => TimeSpan.FromMinutes (3),
					"5m" => TimeSpan.FromMinutes (5),
					"15m" => TimeSpan.FromMinutes (15),
					"30m" => TimeSpan.FromMinutes (30),
					"1h" => TimeSpan.FromHours (1),
					"2h" => TimeSpan.FromHours (2),
					"4h" => TimeSpan.FromHours (4),
					"6h" => TimeSpan.FromHours (6),
					"8h" => TimeSpan.FromHours (8),
					"12h" => TimeSpan.FromHours (12),
					"1d" => TimeSpan.FromDays (1),
					"3d" => TimeSpan.FromDays (3),
					"1w" => TimeSpan.FromDays (7),
					"1M" => TimeSpan.FromDays (30),
					_ => null
					};
			}
		}
	}
