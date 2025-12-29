using System.Globalization;
using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
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

					using var resp = await http.GetAsync (url).ConfigureAwait (false);
					if (!resp.IsSuccessStatusCode)
						{
						Console.WriteLine ($"[binance-1m] HTTP {(int) resp.StatusCode} при загрузке {symbol}, url={url}");
						resp.EnsureSuccessStatusCode ();
						}

					await using var s = await resp.Content.ReadAsStreamAsync ().ConfigureAwait (false);
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s).ConfigureAwait (false);

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

					using var resp = await http.GetAsync (url).ConfigureAwait (false);
					if (!resp.IsSuccessStatusCode)
						{
						Console.WriteLine ($"[binance-6h] HTTP {(int) resp.StatusCode} при загрузке {symbol}, url={url}");
						resp.EnsureSuccessStatusCode ();
						}

					await using var s = await resp.Content.ReadAsStreamAsync ().ConfigureAwait (false);
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s).ConfigureAwait (false);

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

					using var resp = await http.GetAsync (url).ConfigureAwait (false);
					if (!resp.IsSuccessStatusCode)
						{
						Console.WriteLine ($"[binance-1h] HTTP {(int) resp.StatusCode} при загрузке {symbol}, url={url}");
						resp.EnsureSuccessStatusCode ();
						}

					await using var s = await resp.Content.ReadAsStreamAsync ().ConfigureAwait (false);
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s).ConfigureAwait (false);

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

			// Делаем парсер тотально строгим:
			// - никакого "continue" без учёта причины;
			// - любые проблемы формата → ранний fail с примером проблемных элементов;
			// - дубликаты по дню → fail (иначе можно тихо перетирать факт).
			const string url = "https://api.alternative.me/fng/?limit=0";

			static string Short ( string? s, int max )
				{
				if (string.IsNullOrEmpty (s)) return string.Empty;
				return s.Length <= max ? s : s.Substring (0, max) + "...";
				}

			static bool TryReadInt64 ( JsonElement el, out long v )
				{
				v = default;

				if (el.ValueKind == JsonValueKind.Number)
					return el.TryGetInt64 (out v);

				if (el.ValueKind == JsonValueKind.String)
					{
					var s = el.GetString ();
					return long.TryParse (s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
					}

				return false;
				}

			static bool TryReadDoubleInvariant ( JsonElement el, out double v )
				{
				v = default;

				if (el.ValueKind == JsonValueKind.Number)
					return el.TryGetDouble (out v);

				if (el.ValueKind == JsonValueKind.String)
					{
					var s = el.GetString ();
					return double.TryParse (s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
					}

				return false;
				}

			try
				{
				using var resp = await http.GetAsync (url).ConfigureAwait (false);
				if (!resp.IsSuccessStatusCode)
					{
					var body = await resp.Content.ReadAsStringAsync ().ConfigureAwait (false);

					throw new InvalidOperationException (
						$"[fng] HTTP {(int) resp.StatusCode} {resp.ReasonPhrase} при загрузке FNG, " +
						$"url={url}, bodyPrefix='{Short (body, 400)}'");
					}

				await using var s = await resp.Content.ReadAsStreamAsync ().ConfigureAwait (false);
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s).ConfigureAwait (false);

				if (!root.TryGetProperty ("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
					throw new InvalidOperationException ($"[fng] invalid payload: missing/invalid 'data' array. url={url}");

				var dict = new Dictionary<DateTime, double> (capacity: Math.Max (arr.GetArrayLength (), 16));
				var errors = new List<string> ();

				int idx = 0;
				foreach (var el in arr.EnumerateArray ())
					{
					if (!el.TryGetProperty ("timestamp", out var tsEl))
						{
						errors.Add ($"idx={idx}: missing 'timestamp', raw={Short (el.GetRawText (), 300)}");
						idx++;
						continue;
						}

					if (!TryReadInt64 (tsEl, out var ts))
						{
						errors.Add ($"idx={idx}: invalid 'timestamp'={Short (tsEl.GetRawText (), 80)}, raw={Short (el.GetRawText (), 300)}");
						idx++;
						continue;
						}

					// Источник исторически отдаёт seconds, но на практике API иногда мигрируют на ms.
					// Эвристика безопасная: Unix seconds в 2020-х ~ 1.6e9, ms ~ 1.6e12.
					DateTime dtUtc;
					try
						{
						dtUtc = ts >= 1_000_000_000_000L
							? DateTimeOffset.FromUnixTimeMilliseconds (ts).UtcDateTime
							: DateTimeOffset.FromUnixTimeSeconds (ts).UtcDateTime;
						}
					catch (Exception ex)
						{
						errors.Add ($"idx={idx}: timestamp conversion failed ts={ts}, ex={ex.GetType ().Name}, raw={Short (el.GetRawText (), 300)}");
						idx++;
						continue;
						}

					var dayUtc = dtUtc.ToCausalDateUtc ();

					if (!el.TryGetProperty ("value", out var vEl))
						{
						errors.Add ($"idx={idx}: missing 'value' for day={dayUtc:yyyy-MM-dd}, raw={Short (el.GetRawText (), 300)}");
						idx++;
						continue;
						}

					if (!TryReadDoubleInvariant (vEl, out var v))
						{
						errors.Add ($"idx={idx}: invalid 'value'={Short (vEl.GetRawText (), 80)} for day={dayUtc:yyyy-MM-dd}, raw={Short (el.GetRawText (), 300)}");
						idx++;
						continue;
						}

					if (!double.IsFinite (v) || v < 0.0 || v > 100.0)
						{
						errors.Add ($"idx={idx}: out-of-range value={v:G17} for day={dayUtc:yyyy-MM-dd}, raw={Short (el.GetRawText (), 300)}");
						idx++;
						continue;
						}

					if (dict.TryGetValue (dayUtc, out var prev))
						{
						// Дубликаты по дню — потенциальный сигнал изменения формата/семантики источника.
						// Тихо перетирать нельзя: это ломает воспроизводимость.
						errors.Add ($"idx={idx}: duplicate day key {dayUtc:yyyy-MM-dd}: prev={prev:G17}, next={v:G17}");
						idx++;
						continue;
						}

					dict[dayUtc] = v;
					idx++;
					}

				if (errors.Count > 0)
					{
					throw new InvalidOperationException (
						$"[fng] payload parse failed: errors={errors.Count}, url={url}, sample:\n" +
						string.Join ("\n", errors.Take (30)) +
						(errors.Count > 30 ? $"\n... (+{errors.Count - 30} more)" : ""));
					}

				if (dict.Count == 0)
					throw new InvalidOperationException ($"[fng] parsed 0 points: url={url}.");

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

			// Работаем строго в терминах дней (UTC), чтобы чанки не «плыли» из-за времени.
			var startDay = start.ToCausalDateUtc ();
			var endDay = end.ToCausalDateUtc ();

			// На длинном периоде устойчивее резать запросы на куски.
			// Даже если API *в теории* принимает большие диапазоны, на практике часто встречаются обрезания/таймауты.
			const int MaxDaysPerRequest = 370; // ~1 год с запасом

			var dict = new Dictionary<DateTime, double> ();

			try
				{
				for (var cur = startDay; cur <= endDay;)
					{
					var chunkEnd = cur.AddDays (MaxDaysPerRequest - 1);
					if (chunkEnd > endDay) chunkEnd = endDay;

					var chunk = await GetDxySeriesChunk (http, cur, chunkEnd).ConfigureAwait (false);

					foreach (var kv in chunk)
						dict[kv.Key] = kv.Value;

					cur = chunkEnd.AddDays (1);
					}

				return dict;
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[dxy] ошибка при загрузке DXY: {ex}");
				throw;
				}
			}

		private static async Task<Dictionary<DateTime, double>> GetDxySeriesChunk ( HttpClient http, DateTime startDayUtc, DateTime endDayUtc )
			{
			var dict = new Dictionary<DateTime, double> ();

			// Актуальный Frankfurter API: api.frankfurter.dev/v1, параметры base/symbols.
			string symbols = "EUR,JPY,GBP,CAD,SEK,CHF";
			string url =
				$"https://api.frankfurter.dev/v1/{startDayUtc:yyyy-MM-dd}..{endDayUtc:yyyy-MM-dd}?base=USD&symbols={symbols}";

			using var resp = await http.GetAsync (url).ConfigureAwait (false);
			if (!resp.IsSuccessStatusCode)
				{
				Console.WriteLine ($"[dxy] HTTP {(int) resp.StatusCode} при загрузке DXY, url={url}");
				resp.EnsureSuccessStatusCode ();
				}

			await using var s = await resp.Content.ReadAsStreamAsync ().ConfigureAwait (false);
			var root = await JsonSerializer.DeserializeAsync<JsonElement> (s).ConfigureAwait (false);

			if (!root.TryGetProperty ("rates", out var rates) || rates.ValueKind != JsonValueKind.Object)
				return dict;

			foreach (var day in rates.EnumerateObject ())
				{
				if (!DateTime.TryParseExact (
						day.Name,
						"yyyy-MM-dd",
						CultureInfo.InvariantCulture,
						DateTimeStyles.None,
						out var d))
					{
					continue;
					}

				// Нормализуем в каузальную дату UTC (00:00Z).
				d = DateTime.SpecifyKind (d, DateTimeKind.Utc).ToCausalDateUtc ();

				double idx = IndexFromRates (day.Value, out int used);
				if (!double.IsNaN (idx) && used >= 4)
					dict[d] = idx;
				}

			return dict;
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
		public static async Task<List<(DateTime openUtc, double open, double high, double low, double close)>> GetBinanceKlinesRange (
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

					if (diff <= intervalLen.Value + TimeSpan.FromSeconds (5))
						{
						return new List<(DateTime openUtc, double open, double high, double low, double close)> (0);
						}
					}

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

				using var resp = await http.GetAsync (url).ConfigureAwait (false);
				if (!resp.IsSuccessStatusCode)
					{
					Console.WriteLine (
						$"[binance-range] HTTP {(int) resp.StatusCode} при загрузке {symbol} {interval}, url={url}");
					resp.EnsureSuccessStatusCode ();
					}

				await using var s = await resp.Content.ReadAsStreamAsync ().ConfigureAwait (false);
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s).ConfigureAwait (false);

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
