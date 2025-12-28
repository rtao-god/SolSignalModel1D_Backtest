using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps
	{
	/// <summary>
	/// Сканает непрерывность kline-рядов непосредственно через Binance API и детектит пропуски openTime.
	///
	/// Зачем это нужно:
	/// - отделить реальные "серверные" дыры Binance от багов нашего апдейтера/записи/сети;
	/// - получить весь список дыр одним прогоном, а не по одной на запуск;
	/// - сгенерировать готовый C#-сниппет KnownCandleGap для фикса в CandleDataGaps.
	///
	/// Контракт:
	/// - работает в терминах [fromUtc..toUtcExclusive);
	/// - from/to должны быть UTC и выровнены по интервалу (для 1m: секунды=0, миллисекунды=0).
	/// </summary>
	public static class BinanceGapDiscovery
		{
		public sealed record Gap (
			string Symbol,
			string Interval,
			DateTime ExpectedStartUtc,
			DateTime ActualStartUtc,
			int MissingBars )
			{
			public override string ToString ()
				=> $"{Symbol} {Interval}: gap [{ExpectedStartUtc:O}..{ActualStartUtc:O}), missingBars={MissingBars}";
			}

		public sealed record PassResult (
			int PassNo,
			DateTime FromUtc,
			DateTime ToUtcExclusive,
			int Pages,
			int Bars,
			IReadOnlyList<Gap> Gaps );

		public sealed record GapAggregate (
			Gap Gap,
			int SeenInPasses,
			int TotalPasses )
			{
			public bool IsStable => SeenInPasses == TotalPasses;
			}

		public sealed record Report (
			string Symbol,
			string Interval,
			DateTime FromUtc,
			DateTime ToUtcExclusive,
			IReadOnlyList<PassResult> Passes,
			IReadOnlyList<GapAggregate> Aggregates );

		public sealed class Options
			{
			/// <summary>Сколько полных проходов делать. 3 — нормальный компромисс.</summary>
			public int Passes { get; init; } = 3;

			/// <summary>Задержка между страницами, чтобы не упираться в rate-limit.</summary>
			public TimeSpan ThrottleDelay { get; init; } = TimeSpan.FromMilliseconds (120);

			/// <summary>Сколько раз ретраить одну страницу при временной ошибке (429/5xx/timeout).</summary>
			public int MaxPageRetries { get; init; } = 4;

			/// <summary>Базовая пауза для backoff при ретраях.</summary>
			public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds (250);

			/// <summary>
			/// Если true — включить в агрегаты все gaps (stable+flaky).
			/// Если false — агрегаты будут содержать только stable (видимые во всех проходах).
			/// </summary>
			public bool IncludeFlakyInAggregates { get; init; } = true;
			}

		/// <summary>
		/// Основной entrypoint.
		/// </summary>
		public static async Task<Report> RunAsync (
			HttpClient http,
			string symbol,
			string interval,
			DateTime fromUtc,
			DateTime toUtcExclusive,
			Options? opt = null,
			CancellationToken ct = default )
			{
			if (http == null) throw new ArgumentNullException (nameof (http));
			if (string.IsNullOrWhiteSpace (symbol)) throw new ArgumentException ("symbol empty", nameof (symbol));
			if (string.IsNullOrWhiteSpace (interval)) throw new ArgumentException ("interval empty", nameof (interval));

			opt ??= new Options ();

			symbol = symbol.Trim ().ToUpperInvariant ();
			interval = interval.Trim ();

			EnsureUtc (fromUtc, nameof (fromUtc));
			EnsureUtc (toUtcExclusive, nameof (toUtcExclusive));
			if (toUtcExclusive <= fromUtc)
				throw new ArgumentException ("toUtcExclusive must be > fromUtc", nameof (toUtcExclusive));

			var step = TryGetBinanceIntervalLength (interval)
				?? throw new ArgumentOutOfRangeException (nameof (interval), interval, "Unsupported Binance interval.");

			EnsureAlignedOrThrow (fromUtc, step, "fromUtc");
			EnsureAlignedOrThrow (toUtcExclusive, step, "toUtcExclusive");

			var passes = new List<PassResult> (opt.Passes);

			for (int p = 1; p <= opt.Passes; p++)
				{
				ct.ThrowIfCancellationRequested ();

				var pr = await ScanOnceAsync (
					http: http,
					symbol: symbol,
					interval: interval,
					fromUtc: fromUtc,
					toUtcExclusive: toUtcExclusive,
					step: step,
					passNo: p,
					opt: opt,
					ct: ct).ConfigureAwait (false);

				passes.Add (pr);
				}

			var aggs = Aggregate (passes, totalPasses: opt.Passes, includeFlaky: opt.IncludeFlakyInAggregates);

			return new Report (
				Symbol: symbol,
				Interval: interval,
				FromUtc: fromUtc,
				ToUtcExclusive: toUtcExclusive,
				Passes: passes,
				Aggregates: aggs);
			}

		/// <summary>
		/// Генерирует C#-сниппет под CandleDataGaps.Known1mGaps/Known*Gaps.
		/// </summary>
		public static string ToKnownCandleGapCSharp (
			IReadOnlyList<GapAggregate> aggregates,
			bool onlyStable = true,
			string indent = "\t\t\t" )
			{
			if (aggregates == null) throw new ArgumentNullException (nameof (aggregates));

			var xs = aggregates
				.Where (a => !onlyStable || a.IsStable)
				.Select (a => a.Gap)
				.OrderBy (g => g.Symbol, StringComparer.OrdinalIgnoreCase)
				.ThenBy (g => g.Interval, StringComparer.Ordinal)
				.ThenBy (g => g.ExpectedStartUtc)
				.ToList ();

			var sb = new StringBuilder ();

			foreach (var g in xs)
				{
				sb.AppendLine ($"{indent}new KnownCandleGap (");
				sb.AppendLine ($"{indent}\tsymbol: \"{g.Symbol}\",");
				sb.AppendLine ($"{indent}\tinterval: \"{g.Interval}\",");
				sb.AppendLine ($"{indent}\texpectedStartUtc: new DateTime ({g.ExpectedStartUtc.Year}, {g.ExpectedStartUtc.Month}, {g.ExpectedStartUtc.Day}, {g.ExpectedStartUtc.Hour}, {g.ExpectedStartUtc.Minute}, {g.ExpectedStartUtc.Second}, DateTimeKind.Utc),");
				sb.AppendLine ($"{indent}\tactualStartUtc:   new DateTime ({g.ActualStartUtc.Year}, {g.ActualStartUtc.Month}, {g.ActualStartUtc.Day}, {g.ActualStartUtc.Hour}, {g.ActualStartUtc.Minute}, {g.ActualStartUtc.Second}, DateTimeKind.Utc)),");
				sb.AppendLine ();
				}

			return sb.ToString ();
			}

		private static async Task<PassResult> ScanOnceAsync (
			HttpClient http,
			string symbol,
			string interval,
			DateTime fromUtc,
			DateTime toUtcExclusive,
			TimeSpan step,
			int passNo,
			Options opt,
			CancellationToken ct )
			{
			long endExclusiveMs = new DateTimeOffset (toUtcExclusive).ToUnixTimeMilliseconds ();
			long cursorMs = new DateTimeOffset (fromUtc).ToUnixTimeMilliseconds ();

			// Binance endTime - инклюзивный, поэтому ставим endInclusive = endExclusive-1.
			long endInclusiveMs = endExclusiveMs - 1;

			var gaps = new List<Gap> ();
			int pages = 0;
			int bars = 0;

			DateTime? prevOpenUtc = null;

			while (cursorMs < endExclusiveMs)
				{
				ct.ThrowIfCancellationRequested ();

				var page = await FetchKlinesPageWithRetries (
					http: http,
					symbol: symbol,
					interval: interval,
					startMs: cursorMs,
					endMsInclusive: endInclusiveMs,
					maxRetries: opt.MaxPageRetries,
					retryBaseDelay: opt.RetryBaseDelay,
					ct: ct).ConfigureAwait (false);

				pages++;

				if (page.Count == 0)
					{
					// Если в середине диапазона внезапно пусто — это не "дыра", это сбой/лимит/бан.
					// Лучше падать явно, чтобы не генерить фейковую гигантскую дыру.
					var cursorUtc = DateTimeOffset.FromUnixTimeMilliseconds (cursorMs).UtcDateTime;
					throw new InvalidOperationException (
						$"[binance-gap-scan] empty klines page at cursor={cursorUtc:O} " +
						$"for {symbol} {interval}. This indicates API/limit failure, not a deterministic gap.");
					}

				// Проверка непрерывности внутри страницы и с предыдущей страницей.
				for (int i = 0; i < page.Count; i++)
					{
					var curOpenUtc = page[i];

					if (prevOpenUtc == null)
						{
						// Первый бар в скане должен совпасть с курсором (или начать позже — тогда это gap от курсора).
						var expectedFirstUtc = DateTimeOffset.FromUnixTimeMilliseconds (cursorMs).UtcDateTime;
						if (curOpenUtc > expectedFirstUtc)
							{
							AddGap (gaps, symbol, interval, expectedFirstUtc, curOpenUtc, step);
							}

						prevOpenUtc = curOpenUtc;
						bars++;
						continue;
						}

					var expectedUtc = prevOpenUtc.Value + step;

					if (curOpenUtc > expectedUtc)
						{
						AddGap (gaps, symbol, interval, expectedUtc, curOpenUtc, step);
						}
					else if (curOpenUtc < expectedUtc)
						{
						// Это уже не "gap", а нарушение монотонности/дубликаты/пересечение страниц.
						// Нельзя продолжать: результаты будут недетерминированны.
						throw new InvalidOperationException (
							$"[binance-gap-scan] non-ascending/overlapping openTime: prev={prevOpenUtc.Value:O}, cur={curOpenUtc:O} " +
							$"for {symbol} {interval}. Page continuity is broken.");
						}

					prevOpenUtc = curOpenUtc;
					bars++;
					}

				// Двигаем курсор на следующий ожидаемый бар после последнего полученного.
				var lastUtc = prevOpenUtc!.Value;
				var nextUtc = lastUtc + step;
				cursorMs = new DateTimeOffset (nextUtc).ToUnixTimeMilliseconds ();

				if (opt.ThrottleDelay > TimeSpan.Zero)
					await Task.Delay (opt.ThrottleDelay, ct).ConfigureAwait (false);
				}

			return new PassResult (
				PassNo: passNo,
				FromUtc: fromUtc,
				ToUtcExclusive: toUtcExclusive,
				Pages: pages,
				Bars: bars,
				Gaps: gaps);
			}

		private static void AddGap (
			List<Gap> outGaps,
			string symbol,
			string interval,
			DateTime expectedStartUtc,
			DateTime actualStartUtc,
			TimeSpan step )
			{
			if (actualStartUtc <= expectedStartUtc)
				return;

			var diff = actualStartUtc - expectedStartUtc;

			// missing bars = diff/step
			double raw = diff.TotalMilliseconds / step.TotalMilliseconds;
			int missing = (int) Math.Round (raw);

			if (missing <= 0)
				return;

			outGaps.Add (new Gap (
				Symbol: symbol,
				Interval: interval,
				ExpectedStartUtc: expectedStartUtc,
				ActualStartUtc: actualStartUtc,
				MissingBars: missing));
			}

		private static IReadOnlyList<GapAggregate> Aggregate ( List<PassResult> passes, int totalPasses, bool includeFlaky )
			{
			var dict = new Dictionary<(string Symbol, string Interval, DateTime Expected, DateTime Actual, int MissingBars), int> ();

			foreach (var p in passes)
				{
				// В пределах одного прохода один и тот же gap может встретиться максимум один раз,
				// но на всякий случай дедуп по ключу.
				var seenInThisPass = new HashSet<(string, string, DateTime, DateTime, int)> ();

				foreach (var g in p.Gaps)
					{
					var k = (g.Symbol, g.Interval, g.ExpectedStartUtc, g.ActualStartUtc, g.MissingBars);
					if (!seenInThisPass.Add (k)) continue;

					dict.TryGetValue (k, out int c);
					dict[k] = c + 1;
					}
				}

			var aggs = dict
				.Select (kv =>
				{
					var (sym, tf, exp, act, miss) = kv.Key;
					var gap = new Gap (sym, tf, exp, act, miss);
					return new GapAggregate (gap, SeenInPasses: kv.Value, TotalPasses: totalPasses);
				})
				.Where (a => includeFlaky || a.IsStable)
				.OrderByDescending (a => a.IsStable) // stable сверху
				.ThenByDescending (a => a.SeenInPasses)
				.ThenBy (a => a.Gap.ExpectedStartUtc)
				.ToList ();

			return aggs;
			}

		private static async Task<List<DateTime>> FetchKlinesPageWithRetries (
			HttpClient http,
			string symbol,
			string interval,
			long startMs,
			long endMsInclusive,
			int maxRetries,
			TimeSpan retryBaseDelay,
			CancellationToken ct )
			{
			// Binance limit = 1000.
			const int limit = 1000;

			string symbolEsc = Uri.EscapeDataString (symbol);
			string url =
				$"https://api.binance.com/api/v3/klines?symbol={symbolEsc}&interval={interval}&limit={limit}&startTime={startMs}&endTime={endMsInclusive}";

			for (int attempt = 0; attempt <= maxRetries; attempt++)
				{
				ct.ThrowIfCancellationRequested ();

				try
					{
					using var resp = await http.GetAsync (url, ct).ConfigureAwait (false);

					// 429/5xx — временные. 4xx (кроме 429) — считаем фатальными.
					if (!resp.IsSuccessStatusCode)
						{
						int sc = (int) resp.StatusCode;

						if (sc == 429 || sc >= 500)
							throw new HttpRequestException ($"HTTP {sc} for {url}");

						var body = await resp.Content.ReadAsStringAsync (ct).ConfigureAwait (false);
						throw new InvalidOperationException ($"[binance-gap-scan] HTTP {sc} for {symbol} {interval}, url={url}, bodyPrefix='{Short (body, 500)}'");
						}

					await using var s = await resp.Content.ReadAsStreamAsync (ct).ConfigureAwait (false);
					var root = await JsonSerializer.DeserializeAsync<JsonElement> (s, cancellationToken: ct).ConfigureAwait (false);

					if (root.ValueKind != JsonValueKind.Array)
						throw new InvalidOperationException ($"[binance-gap-scan] invalid payload (not array) for url={url}");

					var times = new List<DateTime> (capacity: root.GetArrayLength ());

					foreach (var el in root.EnumerateArray ())
						{
						// Binance kline: [ openTime, open, high, low, close, ... ]
						if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength () == 0)
							continue;

						long openMs = el[0].GetInt64 ();
						var dt = DateTimeOffset.FromUnixTimeMilliseconds (openMs).UtcDateTime;

						// На скане нам важны только openTime.
						times.Add (dt);
						}

					// Binance обычно возвращает уже отсортировано, но это инвариант для нашего сканера.
					for (int i = 1; i < times.Count; i++)
						{
						if (times[i] <= times[i - 1])
							throw new InvalidOperationException (
								$"[binance-gap-scan] non-ascending page payload: tPrev={times[i - 1]:O}, tCur={times[i]:O} for url={url}");
						}

					return times;
					}
				catch (Exception ex) when (attempt < maxRetries && IsRetryable (ex))
					{
					int backoffMs = (int) (retryBaseDelay.TotalMilliseconds * Math.Pow (2, attempt));
					backoffMs = Math.Clamp (backoffMs, 200, 8000);

					await Task.Delay (backoffMs, ct).ConfigureAwait (false);
					continue;
					}
				}

			// unreachable
			throw new InvalidOperationException ("[binance-gap-scan] retries exhausted unexpectedly.");
			}

		private static bool IsRetryable ( Exception ex )
			{
			if (ex is TaskCanceledException) return true; // timeout/cancel
			if (ex is HttpRequestException) return true;
			return false;
			}

		private static string Short ( string? s, int max )
			{
			if (string.IsNullOrEmpty (s)) return string.Empty;
			return s.Length <= max ? s : s.Substring (0, max) + "...";
			}

		private static void EnsureUtc ( DateTime t, string argName )
			{
			if (t.Kind != DateTimeKind.Utc)
				throw new ArgumentException ($"{argName} must be UTC. Got Kind={t.Kind}", argName);
			}

		private static void EnsureAlignedOrThrow ( DateTime tUtc, TimeSpan step, string tag )
			{
			// Для 1m/1h/6h достаточно требовать нулевые секунды/миллисекунды.
			if (tUtc.Second != 0 || tUtc.Millisecond != 0)
				throw new InvalidOperationException ($"[binance-gap-scan] {tag} must be aligned (sec/ms=0). Got {tUtc:O}");

			// Для 1h/6h также полезно требовать кратность часа, чтобы не плодить "искусственные" gaps.
			if (step >= TimeSpan.FromHours (1))
				{
				if (tUtc.Minute != 0)
					throw new InvalidOperationException ($"[binance-gap-scan] {tag} must be aligned to hour boundary. Got {tUtc:O}, step={step}");
				}

			if (step == TimeSpan.FromHours (6))
				{
				if ((tUtc.Hour % 6) != 0)
					throw new InvalidOperationException ($"[binance-gap-scan] {tag} must be aligned to 6h boundary. Got {tUtc:O}");
				}
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
