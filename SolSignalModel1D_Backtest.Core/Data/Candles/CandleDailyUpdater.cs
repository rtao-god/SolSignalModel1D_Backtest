using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Time;

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
	/// Если файла нет — делает полный бэкофилл с указанной даты.
	/// </summary>
	public sealed class CandleDailyUpdater
		{
		private readonly HttpClient _http;
		private readonly string _symbol;
		private readonly int _catchupDays;
		private readonly string _baseDir;

		/// <summary>
		/// Набор включённых таймфреймов для данного символа.
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
		/// Поиск совпадения с whitelist известных дыр по 1m-свечам.
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
		/// Апдейт для одного TF (кроме 1m-weekends):
		/// - тянет klines с Binance;
		/// - проверяет, что внутри ответа нет дыр по времени;
		/// - пишет только будни (IsWeekendUtc() == false) в один файл.
		/// </summary>
		private async Task UpdateOneAsync (
			string binanceInterval,
			TimeSpan tf,
			DateTime? fullBackfillFromUtc = null )
			{
			var path = BuildPath (binanceInterval);
			var store = new CandleNdjsonStore (path);

			DateTime fromUtc;

			var last = store.TryGetLastTimestampUtc ();
			if (last.HasValue)
				fromUtc = last.Value + tf;
			else if (fullBackfillFromUtc.HasValue)
				fromUtc = fullBackfillFromUtc.Value;
			else
				fromUtc = DateTime.UtcNow.ToCausalDateUtc().AddDays (-_catchupDays);

			var toUtc = DateTime.UtcNow;

			if (!fullBackfillFromUtc.HasValue && (toUtc.ToCausalDateUtc() - fromUtc.ToCausalDateUtc()).TotalDays > _catchupDays)
				fromUtc = toUtc.ToCausalDateUtc().AddDays (-_catchupDays);

			var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, binanceInterval, fromUtc, toUtc);
			if (raw.Count == 0)
				return;

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
							$"[candle-updater] {_symbol} {binanceInterval}: BINANCE GAP in response. " +
							$"request=[{fromUtc:O}..{toUtc:O}] prev={prev:O} expected={expected:O} actual={ts:O}");
						}
					}
				prev = ts;
				}

			var filtered = new List<CandleNdjsonStore.CandleLine> (raw.Count);
			foreach (var r in raw)
				{
				if (r.openUtc.IsWeekendUtc ())
					continue;

				filtered.Add (new CandleNdjsonStore.CandleLine (
					r.openUtc,
					r.open,
					r.high,
					r.low,
					r.close));
				}

			if (filtered.Count == 0)
				return;

			store.Append (filtered);
			}

		/// <summary>
		/// Специальный апдейт для 1m:
		/// - читает один диапазон klines с Binance;
		/// - проверяет отсутствие дыр по времени (с учётом известных дыр Binance);
		/// - пишет будни в SYMBOL-1m.ndjson;
		/// - пишет выходные в SYMBOL-1m-weekends.ndjson.
		///
		/// В режиме полного бэкофилла (fullBackfillFromUtc != null) файлы пересоздаются с нуля.
		/// </summary>
		private async Task UpdateOne1mWithWeekendsAsync ( TimeSpan tf, DateTime? fullBackfillFromUtc = null )
			{
			const string interval = "1m";

			var weekdayPath = BuildPath (interval);
			var weekendPath = BuildWeekendsPath (interval);

			// Полный бэкофилл: удаляем старые файлы, чтобы исключить дубликаты и несостыковки.
			if (fullBackfillFromUtc.HasValue)
				{
				if (File.Exists (weekdayPath))
					CandleFsAudit.Delete (weekdayPath, reason: $"{_symbol} 1m fullBackfill: reset weekday file");

				if (File.Exists (weekendPath))
					CandleFsAudit.Delete (weekendPath, reason: $"{_symbol} 1m fullBackfill: reset weekend file");
				}

			var weekdayStore = new CandleNdjsonStore (weekdayPath);
			var weekendStore = new CandleNdjsonStore (weekendPath);

			DateTime fromUtc;

			if (fullBackfillFromUtc.HasValue)
				{
				fromUtc = fullBackfillFromUtc.Value;
				}
			else
				{
				DateTime? lastWeekday = weekdayStore.TryGetLastTimestampUtc ();
				DateTime? lastWeekend = weekendStore.TryGetLastTimestampUtc ();

				DateTime? lastCombined =
					lastWeekday.HasValue && lastWeekend.HasValue
						? (lastWeekday.Value > lastWeekend.Value ? lastWeekday : lastWeekend)
						: lastWeekday ?? lastWeekend;

				if (lastCombined.HasValue)
					fromUtc = lastCombined.Value + tf;
				else
					fromUtc = DateTime.UtcNow.ToCausalDateUtc().AddDays (-_catchupDays);
				}

			var toUtc = DateTime.UtcNow;

			if (!fullBackfillFromUtc.HasValue && (toUtc.ToCausalDateUtc() - fromUtc.ToCausalDateUtc()).TotalDays > _catchupDays)
				fromUtc = toUtc.ToCausalDateUtc().AddDays (-_catchupDays);

			var raw = await DataLoading.GetBinanceKlinesRange (_http, _symbol, interval, fromUtc, toUtc);
			if (raw.Count == 0)
				return;

			// Одна компактная строка на 1m апдейт: режим + диапазон + сколько баров пришло.
			// Это полезно для понимания "вообще идёт ли запрос" и "какого размера ответ",
			// без спама по каждому append.
			var mode = fullBackfillFromUtc.HasValue ? "full" : "tail";
			Console.WriteLine ($"[candle-updater] {_symbol} 1m: mode={mode}, req=[{fromUtc:O}..{toUtc:O}], got={raw.Count}");

			// Проверка непрерывности минут внутри ответа Binance.
			// - известные дыры — логируем 1 строкой и продолжаем;
			// - неизвестные — валим с request-range.
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

						if (TryMatchKnownGap1m (_symbol, interval, expected, ts, out var _))
							{
							Console.WriteLine (
								$"[candle-updater] {_symbol} {interval}: KNOWN DATA GAP. " +
								$"expected={expected:O} actual={ts:O} gapMinutes={gapMinutes}. allowlist -> continue.");
							prev = ts;
							continue;
							}

						throw new InvalidOperationException (
							$"[candle-updater] {_symbol} {interval}: BINANCE GAP in response. " +
							$"request=[{fromUtc:O}..{toUtc:O}] prev={prev:O} expected={expected:O} actual={ts:O} gapMinutes={gapMinutes}");
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
				weekdayStore.Append (weekday);

			if (weekend.Count > 0)
				weekendStore.Append (weekend);
			}

		/// <summary>
		/// Полный апдейт по TF в рамках включённых флагов.
		/// </summary>
		public async Task UpdateAllAsync ( DateTime? fullBackfillFromUtc = null )
			{
			var tasks = new List<Task> ();

			if ((_enabledTf & CandleUpdateTf.M1) != 0)
				tasks.Add (UpdateOne1mWithWeekendsAsync (TimeSpan.FromMinutes (1), fullBackfillFromUtc));

			if ((_enabledTf & CandleUpdateTf.H1) != 0)
				tasks.Add (UpdateOneAsync ("1h", TimeSpan.FromHours (1), fullBackfillFromUtc));

			if ((_enabledTf & CandleUpdateTf.H6) != 0)
				tasks.Add (UpdateOneAsync ("6h", TimeSpan.FromHours (6), fullBackfillFromUtc));

			if (tasks.Count == 0)
				{
				Console.WriteLine ($"[candle-updater] {_symbol}: no TF enabled, skipping.");
				return;
				}

			await Task.WhenAll (tasks);
			}
		}
	}
