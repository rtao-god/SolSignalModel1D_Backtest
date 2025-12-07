using SolSignalModel1D_Backtest.Core.Utils;

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

		private string BuildPath ( string tf ) =>
			Path.Combine (_baseDir, $"{_symbol}-{tf}.ndjson");

		private string BuildWeekendsPath ( string tf ) =>
			Path.Combine (_baseDir, $"{_symbol}-{tf}-weekends.ndjson");

		/// <summary>
		/// Базовый апдейт для одного TF:
		/// - тянет klines с Binance;
		/// - проверяет, что внутри диапазона нет дыр по времени;
		/// - пишет только будни (IsWeekendUtc() == false) в один файл.
		/// Используется для 1h/6h и может использоваться для 1m,
		/// если отдельный файл выходных не нужен.
		/// </summary>
		private async Task UpdateOneAsync (
			string binanceInterval,
			TimeSpan tf,
			DateTime? fullBackfillFromUtc = null )
			{
			var path = BuildPath (binanceInterval);
			var store = new CandleNdjsonStore (path);

			DateTime fromUtc;
				{
				DateTime? last = store.TryGetLastTimestampUtc ();

				if (last.HasValue)
					fromUtc = last.Value + tf;
				else if (fullBackfillFromUtc.HasValue)
					fromUtc = fullBackfillFromUtc.Value;
				else
					fromUtc = DateTime.UtcNow.Date.AddDays (-_catchupDays);

				var toUtc = DateTime.UtcNow;

				if (!fullBackfillFromUtc.HasValue && (toUtc.Date - fromUtc.Date).TotalDays > _catchupDays)
					fromUtc = toUtc.Date.AddDays (-_catchupDays);

				var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, binanceInterval, fromUtc, toUtc);
				if (raw.Count == 0) return;

				// Проверяем, что внутри полученного диапазона нет дыр по tf.
				// Здесь проверяются и будни, и выходные — если Binance вернул неполный ряд.
				DateTime? prev = null;
				foreach (var r in raw)
					{
					var ts = r.openUtc;
					if (prev.HasValue)
						{
						var expected = prev.Value + tf;
						if (ts != expected)
							{
							throw new InvalidOperationException (
								$"[candle-updater] {_symbol} {binanceInterval}: пропущены свечи между {prev:O} и {ts:O}, ожидали {expected:O}");
							}
						}
					prev = ts;
					}

				var filtered = new List<CandleNdjsonStore.CandleLine> (raw.Count);
				foreach (var r in raw)
					{
					// Для базового файла продолжаем отбрасывать выходные.
					if (r.openUtc.IsWeekendUtc ()) continue;

					filtered.Add (new CandleNdjsonStore.CandleLine (
						r.openUtc,
						r.open,
						r.high,
						r.low,
						r.close));
					}

				if (filtered.Count == 0) return;

				store.Append (filtered);
				}
			}

		/// <summary>
		/// Специальный апдейт для 1m:
		/// - читает один диапазон klines с Binance;
		/// - проверяет отсутствие дыр по времени;
		/// - пишет будни в SYMBOL-1m.ndjson;
		/// - пишет выходные в SYMBOL-1m-weekends.ndjson.
		/// </summary>
		private async Task UpdateOne1mWithWeekendsAsync ( TimeSpan tf, DateTime? fullBackfillFromUtc = null )
			{
			const string interval = "1m";

			var weekdayPath = BuildPath (interval);
			var weekendPath = BuildWeekendsPath (interval);

			var weekdayStore = new CandleNdjsonStore (weekdayPath);
			var weekendStore = new CandleNdjsonStore (weekendPath);

			// Для определения диапазона достаточно любой из серий (будни/выходные),
			// берём максимальный из двух timestamps, чтобы не перезаписывать историю.
			DateTime? lastWeekday = weekdayStore.TryGetLastTimestampUtc ();
			DateTime? lastWeekend = weekendStore.TryGetLastTimestampUtc ();

			DateTime? lastCombined =
				lastWeekday.HasValue && lastWeekend.HasValue
					? (lastWeekday.Value > lastWeekend.Value ? lastWeekday : lastWeekend)
					: lastWeekday ?? lastWeekend;

			DateTime fromUtc;
			if (lastCombined.HasValue)
				fromUtc = lastCombined.Value + tf;
			else if (fullBackfillFromUtc.HasValue)
				fromUtc = fullBackfillFromUtc.Value;
			else
				fromUtc = DateTime.UtcNow.Date.AddDays (-_catchupDays);

			var toUtc = DateTime.UtcNow;

			if (!fullBackfillFromUtc.HasValue && (toUtc.Date - fromUtc.Date).TotalDays > _catchupDays)
				fromUtc = toUtc.Date.AddDays (-_catchupDays);

			var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, interval, fromUtc, toUtc);
			if (raw.Count == 0) return;

			// Проверка непрерывности минут внутри полученного диапазона.
			DateTime? prev = null;
			foreach (var r in raw)
				{
				var ts = r.openUtc;
				if (prev.HasValue)
					{
					var expected = prev.Value + tf;
					if (ts != expected)
						{
						throw new InvalidOperationException (
							$"[candle-updater] {_symbol} {interval}: пропущены 1m-свечи между {prev:O} и {ts:O}, ожидали {expected:O}");
						}
					}
				prev = ts;
				}

			var weekday = new List<CandleNdjsonStore.CandleLine> (raw.Count);
			var weekend = new List<CandleNdjsonStore.CandleLine> (raw.Count);

			foreach (var r in raw)
				{
				var line = new CandleNdjsonStore.CandleLine (
					r.openUtc,
					r.open,
					r.high,
					r.low,
					r.close);

				if (r.openUtc.IsWeekendUtc ())
					weekend.Add (line);
				else
					weekday.Add (line);
				}

			if (weekday.Count > 0)
				{
				weekdayStore.Append (weekday);
				Console.WriteLine (
					$"[candle-updater] {_symbol} {interval}: appended WEEKDAY {weekday.Count} candles");
				}

			if (weekend.Count > 0)
				{
				weekendStore.Append (weekend);
				Console.WriteLine (
					$"[candle-updater] {_symbol} {interval}: appended WEEKEND {weekend.Count} candles");
				}
			}

		/// <summary>
		/// Полный апдейт по трём ТФ.
		/// - 1m: будни + отдельный weekend-файл;
		/// </summary>
		public async Task UpdateAllAsync ( DateTime? fullBackfillFromUtc = null )
			{
			var tasks = new[]
				{
				// 1m: пишем и будний, и weekend-файл.
				UpdateOne1mWithWeekendsAsync (TimeSpan.FromMinutes (1), fullBackfillFromUtc),

				// 1h/6h
				UpdateOneAsync ("1h", TimeSpan.FromHours (1), fullBackfillFromUtc),
				UpdateOneAsync ("6h", TimeSpan.FromHours (6), fullBackfillFromUtc)
				};

			await Task.WhenAll (tasks);
			}
		}
	}
