using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public static class DataLoading
		{
		// для DXY
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
			int max,
			bool allowNull = false )
			{
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
					string url = $"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval=1m&limit={need}";
					if (endTimeMs.HasValue)
						url += $"&endTime={endTimeMs.Value}";

					using var resp = await http.GetAsync (url);
					if (!resp.IsSuccessStatusCode)
						{
						if (allowNull)
							return all;
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

					const long oneMinuteMs = 1L * 60 * 1000;
					endTimeMs = earliestOpenMs - oneMinuteMs;

					if (all.Count >= max)
						break;
					}

				all.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
				if (all.Count > max)
					all = all.GetRange (all.Count - max, max);

				return all;
				}
			catch
				{
				if (allowNull)
					return new List<Candle1m> ();
				throw;
				}
			}

		// ===== 6h =====
		public static async Task<List<Candle6h>> GetBinance6h (
			HttpClient http,
			string symbol,
			int max,
			bool allowNull = false )
			{
			symbol = (symbol ?? string.Empty).Trim ().ToUpperInvariant ();
			int ws = symbol.IndexOfAny (new[] { ' ', '\t', '\r', '\n' });
			if (ws >= 0) symbol = symbol.Substring (0, ws);
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
						if (allowNull)
							return all;
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
			catch
				{
				if (allowNull)
					return new List<Candle6h> ();
				throw;
				}
			}

		// ===== 1h =====
		public static async Task<List<Candle1h>> GetBinance1h (
			HttpClient http,
			string symbol,
			int max,
			bool allowNull = false )
			{
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
						if (allowNull)
							return all;
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
			catch
				{
				if (allowNull)
					return new List<Candle1h> ();
				throw;
				}
			}

		// ===== FNG / DXY / extra ===== 

		public static async Task<Dictionary<DateTime, int>> GetFngHistory ( HttpClient http )
			{
			var dict = new Dictionary<DateTime, int> ();
			try
				{
				string url = "https://api.alternative.me/fng/?limit=1000";
				using var resp = await http.GetAsync (url);
				resp.EnsureSuccessStatusCode ();
				await using var s = await resp.Content.ReadAsStreamAsync ();
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);
				if (root.TryGetProperty ("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
					{
					foreach (var el in arr.EnumerateArray ())
						{
						if (!el.TryGetProperty ("timestamp", out var tsEl)) continue;
						if (!long.TryParse (tsEl.GetString (), out long ts)) continue;
						DateTime d = DateTimeOffset.FromUnixTimeSeconds (ts).UtcDateTime.Date;
						if (el.TryGetProperty ("value", out var vEl) && int.TryParse (vEl.GetString (), out int v))
							dict[d] = v;
						}
					}
				}
			catch
				{
				}
			return dict;
			}

		public static async Task<Dictionary<DateTime, double>> GetDxySeries ( HttpClient http, DateTime start, DateTime end )
			{
			var dict = new Dictionary<DateTime, double> ();
			try
				{
				string to = "EUR,JPY,GBP,CAD,SEK,CHF";
				string url = $"https://api.frankfurter.app/{start:yyyy-MM-dd}..{end:yyyy-MM-dd}?from=USD&to={to}";
				using var resp = await http.GetAsync (url);
				resp.EnsureSuccessStatusCode ();
				await using var s = await resp.Content.ReadAsStreamAsync ();
				var root = await JsonSerializer.DeserializeAsync<JsonElement> (s);
				if (root.TryGetProperty ("rates", out var rates))
					{
					foreach (var day in rates.EnumerateObject ())
						{
						if (!DateTime.TryParse (day.Name, out var d))
							continue;
						double idx = IndexFromRates (day.Value, out int used);
						if (!double.IsNaN (idx) && used >= 4)
							dict[d.Date] = idx;
						}
					}
				}
			catch
				{
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
			try
				{
				if (!File.Exists (path)) return null;
				var txt = File.ReadAllText (path);
				var arr = JsonSerializer.Deserialize<List<ExtraPoint>> (txt);
				if (arr == null) return null;
				var dict = new Dictionary<DateTime, (double, double)> ();
				foreach (var e in arr)
					dict[e.Date.Date] = (e.Funding, e.OI);
				Console.WriteLine ($"[extra] загружено {dict.Count} строк доп. данных");
				return dict;
				}
			catch
				{
				Console.WriteLine ("[extra] не удалось загрузить доп. данные");
				return null;
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
					resp.EnsureSuccessStatusCode ();

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
					break;

				cursor = maxTs + 1;
				}

			result.Sort (( a, b ) => a.openUtc.CompareTo (b.openUtc));
			return result;
			}
		}
	}
