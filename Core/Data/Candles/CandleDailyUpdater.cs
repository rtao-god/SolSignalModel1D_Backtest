using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// Обновляет/создаёт ndjson со свечами.
	/// Если файл уже есть — догоняет последние дни.
	/// Если файла нет — делает полный бэкап с указанной даты.
	/// </summary>
	public sealed class CandleDailyUpdater
		{
		private readonly HttpClient _http;
		private readonly string _symbol;
		private readonly int _catchupDays;
		private readonly string _baseDir;

		public CandleDailyUpdater (
			HttpClient http,
			string symbol,
			string baseDir,
			int catchupDays = 3 )
			{
			_http = http;
			_symbol = symbol;
			_baseDir = baseDir;
			_catchupDays = catchupDays;
			}

		private string BuildPath ( string tf )
			=> Path.Combine (_baseDir, $"{_symbol}-{tf}.ndjson");

		/// <summary>
		/// Полный апдейт по трём ТФ.
		/// Если файла нет — берём fromUtc и тянем до сейчас.
		/// Если файл есть — только догоняем.
		/// </summary>
		public async Task UpdateAllAsync ( DateTime? fullBackfillFromUtc = null )
			{
			await UpdateOneAsync ("1m", TimeSpan.FromMinutes (1), fullBackfillFromUtc);
			await UpdateOneAsync ("1h", TimeSpan.FromHours (1), fullBackfillFromUtc);
			await UpdateOneAsync ("6h", TimeSpan.FromHours (6), fullBackfillFromUtc);
			}

		public async Task UpdateSelectiveAsync ( IEnumerable<string> intervals, DateTime? fullBackfillFromUtc = null )
			{
			foreach (var iv in intervals)
				{
				switch (iv)
					{
					case "1m": await UpdateOneAsync ("1m", TimeSpan.FromMinutes (1), fullBackfillFromUtc); break;
					case "1h": await UpdateOneAsync ("1h", TimeSpan.FromHours (1), fullBackfillFromUtc); break;
					case "6h": await UpdateOneAsync ("6h", TimeSpan.FromHours (6), fullBackfillFromUtc); break;
					default: throw new ArgumentException ($"unsupported interval: {iv}");
					}
				}
			}

		private async Task UpdateOneAsync ( string binanceInterval, TimeSpan tf, DateTime? fullBackfillFromUtc = null )
			{
			var store = new CandleNdjsonStore (BuildPath (binanceInterval));

			// если файл есть — докачиваем с последней свечи; если нет — полный бэкофилл с fullBackfillFromUtc (если задан)
			DateTime? last = store.TryGetLastTimestampUtc ();
			DateTime fromUtc;
			if (last.HasValue)
				fromUtc = last.Value + tf;
			else if (fullBackfillFromUtc.HasValue)
				fromUtc = fullBackfillFromUtc.Value;
			else
				fromUtc = DateTime.UtcNow.Date.AddDays (-_catchupDays);

			DateTime toUtc = DateTime.UtcNow;

			// ограничение догонки по дням — только если НЕ полный бэкофилл
			if (!fullBackfillFromUtc.HasValue && (toUtc.Date - fromUtc.Date).TotalDays > _catchupDays)
				fromUtc = toUtc.Date.AddDays (-_catchupDays);

			var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, binanceInterval, fromUtc, toUtc);
			if (raw.Count == 0) return;

			var filtered = new List<CandleNdjsonStore.CandleLine> (raw.Count);
			foreach (var r in raw)
				{
				if (r.openUtc.IsWeekendUtc ()) continue; // выкидываем выходные
				filtered.Add (new CandleNdjsonStore.CandleLine (r.openUtc, r.open, r.high, r.low, r.close));
				}
			if (filtered.Count == 0) return;

			store.Append (filtered);
			Console.WriteLine ($"[candle-updater] {_symbol} {binanceInterval}: appended {filtered.Count} candles ({fromUtc:yyyy-MM-dd HH:mm} .. {toUtc:yyyy-MM-dd HH:mm} UTC)");
			}

		public async Task UpdateSelectiveAsync ( IEnumerable<string> intervals )
			{
			foreach (var iv in intervals)
				{
				switch (iv)
					{
					case "1m": await UpdateOneAsync ("1m", TimeSpan.FromMinutes (1)); break;
					case "1h": await UpdateOneAsync ("1h", TimeSpan.FromHours (1)); break;
					case "6h": await UpdateOneAsync ("6h", TimeSpan.FromHours (6)); break;
					default: throw new ArgumentException ($"unsupported interval: {iv}");
					}
				}
			}
		}
	}
