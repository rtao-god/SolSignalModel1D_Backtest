using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// Набор флагов: какие таймфреймы обновлять для символа.
	/// Позволяет тонко отключать ненужные TF (например, 1m по PAXG).
	/// </summary>
	[Flags]
	public enum CandleUpdateTf
		{
		None = 0,
		M1 = 1 << 0,
		H1 = 1 << 1,
		H6 = 1 << 2,
		All = M1 | H1 | H6
		}

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

		/// <summary>
		/// Набор включённых таймфреймов для данного символа.
		/// Например:
		/// - All: 1m+1h+6h
		/// - M1 | H6: минутки и 6h, без 1h
		/// - H6: только 6h
		/// </summary>
		private readonly CandleUpdateTf _enabledTf;

		public CandleDailyUpdater (
			HttpClient http,
			string symbol,
			string baseDir,
			int catchupDays = 3,
			CandleUpdateTf enabledTf = CandleUpdateTf.All )
			{
			_http = http ?? throw new ArgumentNullException (nameof (http));
			_symbol = symbol ?? throw new ArgumentNullException (nameof (symbol));
			_baseDir = baseDir ?? throw new ArgumentNullException (nameof (baseDir));
			_catchupDays = catchupDays;
			_enabledTf = enabledTf;
			}

		private string BuildPath ( string tf ) =>
			Path.Combine (_baseDir, $"{_symbol}-{tf}.ndjson");

		private string BuildWeekendsPath ( string tf ) =>
			Path.Combine (_baseDir, $"{_symbol}-{tf}-weekends.ndjson");

		/// <summary>
		/// Поиск совпадения с "whitelist" известных дыр по 1m-свечам.
		/// </summary>
		private static bool TryMatchKnownGap1m (
			string symbol,
			string interval,
			DateTime expected,
			DateTime actual,
			out KnownCandleGap gap )
			{
			foreach (var g in CandleDataGaps.Known1mGaps)
				{
				if (!string.Equals (g.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
					continue;
				if (!string.Equals (g.Interval, interval, StringComparison.OrdinalIgnoreCase))
					continue;
				if (g.ExpectedStartUtc != expected)
					continue;
				if (g.ActualStartUtc != actual)
					continue;

				gap = g;
				return true;
				}

			gap = default;
			return false;
			}

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

				// В "хвостовом" режиме ограничиваемся catchupDays,
				// чтобы не тянуть огромный диапазон.
				if (!fullBackfillFromUtc.HasValue && (toUtc.Date - fromUtc.Date).TotalDays > _catchupDays)
					fromUtc = toUtc.Date.AddDays (-_catchupDays);

				var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, binanceInterval, fromUtc, toUtc);
				if (raw.Count == 0) return;

				// Проверяем непрерывность по tf, без каких-либо whitelist — здесь дыр не ожидается.
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
		/// - проверяет отсутствие дыр по времени (с учётом известных дыр Binance);
		/// - пишет будни в SYMBOL-1m.ndjson;
		/// - пишет выходные в SYMBOL-1m-weekends.ndjson.
		///
		/// В режиме полного бэкофилла (fullBackfillFromUtc != null) файлы 1m и 1m-weekends
		/// пересоздаются с нуля, а диапазон начинается строго с fullBackfillFromUtc.
		/// </summary>
		private async Task UpdateOne1mWithWeekendsAsync ( TimeSpan tf, DateTime? fullBackfillFromUtc = null )
			{
			const string interval = "1m";

			var weekdayPath = BuildPath (interval);
			var weekendPath = BuildWeekendsPath (interval);

			// Полный бэкофилл: удаляем старые файлы, чтобы гарантированно
			// получить непрерывную историю с FullBackfillFromUtc и без дубликатов.
			if (fullBackfillFromUtc.HasValue)
				{
				if (File.Exists (weekdayPath)) File.Delete (weekdayPath);
				if (File.Exists (weekendPath)) File.Delete (weekendPath);
				}

			var weekdayStore = new CandleNdjsonStore (weekdayPath);
			var weekendStore = new CandleNdjsonStore (weekendPath);

			DateTime fromUtc;

			if (fullBackfillFromUtc.HasValue)
				{
				// Режим полного бэкофилла: игнорируем существующую историю,
				// тянем всё с заданной даты.
				fromUtc = fullBackfillFromUtc.Value;
				}
			else
				{
				// Для определения диапазона в "хвостовом" режиме достаточно любой из серий (будни/выходные),
				// берём максимальный из двух timestamps, чтобы не перезаписывать историю.
				DateTime? lastWeekday = weekdayStore.TryGetLastTimestampUtc ();
				DateTime? lastWeekend = weekendStore.TryGetLastTimestampUtc ();

				DateTime? lastCombined =
					lastWeekday.HasValue && lastWeekend.HasValue
						? (lastWeekday.Value > lastWeekend.Value ? lastWeekday : lastWeekend)
						: lastWeekday ?? lastWeekend;

				if (lastCombined.HasValue)
					fromUtc = lastCombined.Value + tf;
				else
					fromUtc = DateTime.UtcNow.Date.AddDays (-_catchupDays);
				}

			var toUtc = DateTime.UtcNow;

			// Только в "хвостовом" режиме режем диапазон до catchupDays.
			if (!fullBackfillFromUtc.HasValue && (toUtc.Date - fromUtc.Date).TotalDays > _catchupDays)
				fromUtc = toUtc.Date.AddDays (-_catchupDays);

			Console.WriteLine (
				$"[candle-updater] {_symbol} {interval}: requesting klines [{fromUtc:O}..{toUtc:O}] " +
				$"(fullBackfillFromUtc={(fullBackfillFromUtc.HasValue ? fullBackfillFromUtc.Value.ToString ("O") : "null")})");

			var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, interval, fromUtc, toUtc);
			if (raw.Count == 0)
				{
				Console.WriteLine (
					$"[candle-updater] {_symbol} {interval}: received 0 klines for [{fromUtc:O}..{toUtc:O}]");
				return;
				}

			var firstTs = raw[0].openUtc;
			var lastTs = raw[^1].openUtc;

			Console.WriteLine (
				$"[candle-updater] {_symbol} {interval}: received {raw.Count} klines, " +
				$"ts-range=[{firstTs:O}..{lastTs:O}]");

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
						var gapMinutes = (ts - expected).TotalMinutes;

						// Сначала проверяем, не попали ли ровно в заранее известную дыру Binance.
						if (TryMatchKnownGap1m (_symbol, interval, expected, ts, out var _))
							{
							Console.WriteLine (
								$"[candle-updater] {_symbol} {interval}: KNOWN DATA GAP detected. " +
								$"expected={expected:O}, actual={ts:O}, gapMinutes={gapMinutes}. " +
								$"Gap is whitelisted, продолжаем без synthetic fill.");

							// Дырка остаётся в данных, но пайплайн не падает.
							prev = ts;
							continue;
							}

						// Любая НЕизвестная дыра остаётся жёсткой ошибкой.
						Console.WriteLine (
							$"[candle-updater] {_symbol} {interval}: GAP detected. " +
							$"prev={prev:O}, expected={expected:O}, actual={ts:O}, gapMinutes={gapMinutes}");

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
		/// Полный апдейт по трём ТФ (в рамках включённых флагов).
		/// - 1m: будни + отдельный weekend-файл (если TF.M1 включён);
		/// - 1h/6h — стандартный путь.
		/// </summary>
		public async Task UpdateAllAsync ( DateTime? fullBackfillFromUtc = null )
			{
			var tasks = new List<Task> ();

			// 1m: пишем и будний, и weekend-файл (если TF включён)
			if ((_enabledTf & CandleUpdateTf.M1) != 0)
				{
				tasks.Add (UpdateOne1mWithWeekendsAsync (TimeSpan.FromMinutes (1), fullBackfillFromUtc));
				}

			// 1h
			if ((_enabledTf & CandleUpdateTf.H1) != 0)
				{
				tasks.Add (UpdateOneAsync ("1h", TimeSpan.FromHours (1), fullBackfillFromUtc));
				}

			// 6h
			if ((_enabledTf & CandleUpdateTf.H6) != 0)
				{
				tasks.Add (UpdateOneAsync ("6h", TimeSpan.FromHours (6), fullBackfillFromUtc));
				}

			if (tasks.Count == 0)
				{
				Console.WriteLine ($"[candle-updater] {_symbol}: no TF enabled, skipping.");
				return;
				}

			await Task.WhenAll (tasks);
			}
		}
	}
