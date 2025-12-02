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
		private async Task UpdateOneAsync ( string binanceInterval, TimeSpan tf, DateTime? fullBackfillFromUtc = null )
			{
			var path = BuildPath (binanceInterval);
			Console.WriteLine ($"[candle-updater] {_symbol} {binanceInterval}: path={path}");

			var store = new CandleNdjsonStore (path);

			DateTime? last = store.TryGetLastTimestampUtc ();
			DateTime fromUtc;
			if (last.HasValue)
				fromUtc = last.Value + tf;
			else if (fullBackfillFromUtc.HasValue)
				fromUtc = fullBackfillFromUtc.Value;
			else
				fromUtc = DateTime.UtcNow.Date.AddDays (-_catchupDays);

			DateTime toUtc = DateTime.UtcNow;

			if (!fullBackfillFromUtc.HasValue && (toUtc.Date - fromUtc.Date).TotalDays > _catchupDays)
				fromUtc = toUtc.Date.AddDays (-_catchupDays);

			Console.WriteLine ($"[candle-updater] {_symbol} {binanceInterval}: last={last?.ToString ("yyyy-MM-dd HH:mm") ?? "null"}, from={fromUtc:yyyy-MM-dd HH:mm}, to={toUtc:yyyy-MM-dd HH:mm}");

			var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, binanceInterval, fromUtc, toUtc);
			if (raw.Count == 0) return;

			var filtered = new List<CandleNdjsonStore.CandleLine> (raw.Count);
			foreach (var r in raw)
				{
				if (r.openUtc.IsWeekendUtc ()) continue;
				filtered.Add (new CandleNdjsonStore.CandleLine (r.openUtc, r.open, r.high, r.low, r.close));
				}
			if (filtered.Count == 0) return;

			store.Append (filtered);
			Console.WriteLine ($"[candle-updater] {_symbol} {binanceInterval}: appended {filtered.Count} candles ({fromUtc:yyyy-MM-dd HH:mm} .. {toUtc:yyyy-MM-dd HH:mm} UTC)");
			}

		public async Task UpdateAllAsync ( DateTime? fullBackfillFromUtc = null )
			{
			Console.WriteLine ($"[candle-updater] symbol={_symbol}, baseDir={_baseDir}, fullBackfillFrom={fullBackfillFromUtc:yyyy-MM-dd HH:mm}");

			var tasks = new[]
			{
		UpdateOneAsync ("1m", TimeSpan.FromMinutes (1), fullBackfillFromUtc),
		UpdateOneAsync ("1h", TimeSpan.FromHours (1), fullBackfillFromUtc),
		UpdateOneAsync ("6h", TimeSpan.FromHours (6), fullBackfillFromUtc)
	};

			await Task.WhenAll (tasks);
			}
		}
	}
