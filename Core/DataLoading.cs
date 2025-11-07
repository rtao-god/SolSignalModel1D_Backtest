using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core
	{
	public static class DataLoading
		{
		// для DXY как раньше
		private static readonly Dictionary<string, double> DxyWeights = new ()
			{
			["EUR"] = 57.6,
			["JPY"] = 13.6,
			["GBP"] = 11.9,
			["CAD"] = 9.1,
			["SEK"] = 4.2,
			["CHF"] = 3.6
			};

		/// <summary>
		/// Тянет с Binance 6h-свечи с пагинацией НАЗАД по времени.
		/// Binance по умолчанию отдаёт последние N свечей, поэтому мы:
		/// 1) берём последние,
		/// 2) узнаём самый ранний openTime из пачки,
		/// 3) следующий запрос делаем с &endTime=раньше_на_6ч
		/// </summary>
		/// <param name="http">HttpClient</param>
		/// <param name="symbol">"SOLUSDT" и т.п.</param>
		/// <param name="max">сколько максимум свечей хотим</param>
		/// <param name="allowNull">если true — при ошибке вернём пустой список</param>
		public static async Task<List<Candle6h>> GetBinance6h ( HttpClient http, string symbol, int max, bool allowNull = false )
			{
			const int chunk = 1000;
			var all = new List<Candle6h> (max);

			// будем идти назад
			long? endTimeMs = null;

			try
				{
				while (all.Count < max)
					{
					int need = Math.Min (chunk, max - all.Count);

					string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=6h&limit={need}";
					if (endTimeMs.HasValue)
						url += $"&endTime={endTimeMs.Value}";

					using var resp = await http.GetAsync (url);
					resp.EnsureSuccessStatusCode ();
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

					// Binance уже отдаёт по возрастанию внутри пачки, но мы всё равно отсортируем
					batch.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));

					// мы идём назад, поэтому новые (более старые) свечи надо класть В НАЧАЛО,
					// чтобы в конце всё было по времени
					all.InsertRange (0, batch);

					// готовимся идти ещё НА РАНЬШЕ на 6 часов
					const long sixHoursMs = 6L * 60 * 60 * 1000;
					endTimeMs = earliestOpenMs - sixHoursMs;

					// вдруг уже набрали
					if (batch.Count < need)
						break;
					}

				// итоговая сортировка на всякий случай
				all.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));

				return all;
				}
			catch
				{
				if (allowNull)
					return new List<Candle6h> ();
				throw;
				}
			}

		/// <summary>
		/// Fear & Greed как было.
		/// </summary>
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
				// проглотим — потом просто не будет FNG
				}
			return dict;
			}

		/// <summary>
		/// DXY через frankfurter (как раньше).
		/// </summary>
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
				// проглотим — потом просто не будет DXY
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

		/// <summary>
		/// твои extra.json (funding + OI)
		/// </summary>
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

		private struct ExtraPoint
			{
			public DateTime Date { get; set; }
			public double Funding { get; set; }
			public double OI { get; set; }
			}
		}
	}
