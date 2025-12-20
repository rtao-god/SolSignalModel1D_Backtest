using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Data.Indicators
	{
	/// <summary>
	/// Обновляет NDJSON-кэш индикаторов (FNG, DXY).
	///
	/// Политика (вариант A):
	/// - если требуемый старт раньше, чем первая дата в сторе — делаем rebuild/backfill (перезапись файла);
	/// - иначе — дописываем хвост (append).
	///
	/// Инварианты:
	/// - дыры/невалидные значения — фатальны (никаких "заглушек");
	/// - NeutralFill допускается только как carry-forward уже известного валидного факта.
	/// </summary>
	public sealed class IndicatorsDailyUpdater
		{
		public enum FillMode
			{
			Strict = 0,
			NeutralFill = 1
			}

		private readonly HttpClient _http;
		private readonly IndicatorsNdjsonStore _fngStore;
		private readonly IndicatorsNdjsonStore _dxyStore;

		private readonly string _fngPath;
		private readonly string _dxyPath;

		/// <summary>
		/// Для DXY в режиме NeutralFill нужен опорный "последний известный факт".
		/// Чтобы стартовать carry-forward, тянем небольшой буфер назад.
		/// </summary>
		private const int DxyBootstrapLookbackDays = 14;

		public IndicatorsDailyUpdater ( HttpClient http )
			{
			_http = http ?? throw new ArgumentNullException (nameof (http));

			_fngPath = Path.Combine (PathConfig.IndicatorsDir, "fng.ndjson");
			_dxyPath = Path.Combine (PathConfig.IndicatorsDir, "dxy.ndjson");

			_fngStore = new IndicatorsNdjsonStore (_fngPath);
			_dxyStore = new IndicatorsNdjsonStore (_dxyPath);
			}

		public async Task UpdateAllAsync (
			DateTime rangeStartUtc,
			DateTime rangeEndUtc,
			FillMode fngFillMode,
			FillMode dxyFillMode )
			{
			await UpdateFngAsync (rangeStartUtc, rangeEndUtc, fngFillMode);
			await UpdateDxyAsync (rangeStartUtc, rangeEndUtc, dxyFillMode);
			}

		public Task UpdateAllAsync ( DateTime rangeStartUtc, DateTime rangeEndUtc, FillMode fillMode )
			=> UpdateAllAsync (rangeStartUtc, rangeEndUtc, fngFillMode: fillMode, dxyFillMode: fillMode);

		public void EnsureCoverageOrFail ( DateTime rangeStartUtc, DateTime rangeEndUtc )
			{
			var start = rangeStartUtc.ToCausalDateUtc ();
			var end = rangeEndUtc.ToCausalDateUtc ();

			var fng = _fngStore.ReadRange (rangeStartUtc, rangeEndUtc);
			var dxy = _dxyStore.ReadRange (rangeStartUtc, rangeEndUtc);

			var missing = new List<string> ();
			var invalid = new List<string> ();

			static string Describe ( Dictionary<DateTime, double> m )
				{
				if (m.Count == 0) return "empty";
				var min = m.Keys.Min ();
				var max = m.Keys.Max ();
				return $"count={m.Count}, range=[{min:yyyy-MM-dd}..{max:yyyy-MM-dd}]";
				}

			Console.WriteLine (
				$"[indicators] ensure: check=[{start:yyyy-MM-dd}..{end:yyyy-MM-dd}], " +
				$"fng={Describe (fng)}, dxy={Describe (dxy)}");

			for (var d = start; d <= end; d = d.AddDays (1))
				{
				if (!fng.TryGetValue (d, out var fngVal))
					missing.Add ($"FNG@{d:yyyy-MM-dd}");
				else if (!double.IsFinite (fngVal) || fngVal < 0.0 || fngVal > 100.0)
					invalid.Add ($"FNG@{d:yyyy-MM-dd}={fngVal}");

				if (!dxy.TryGetValue (d, out var dxyVal))
					missing.Add ($"DXY@{d:yyyy-MM-dd}");
				else if (!double.IsFinite (dxyVal) || dxyVal <= 0.0)
					invalid.Add ($"DXY@{d:yyyy-MM-dd}={dxyVal}");
				}

			if (missing.Count > 0)
				throw new InvalidOperationException ("[indicators] missing: " + string.Join (", ", missing));

			if (invalid.Count > 0)
				throw new InvalidOperationException ("[indicators] invalid values: " + string.Join (", ", invalid));
			}

		public Dictionary<DateTime, double> LoadFngDict ( DateTime startUtc, DateTime endUtc )
			{
			var raw = _fngStore.ReadRange (startUtc, endUtc);
			var res = new Dictionary<DateTime, double> (raw.Count);

			foreach (var kv in raw)
				res[kv.Key] = (int) Math.Round (kv.Value);

			return res;
			}

		public Dictionary<DateTime, double> LoadDxyDict ( DateTime startUtc, DateTime endUtc )
			=> _dxyStore.ReadRange (startUtc, endUtc);

		private static bool IsValidFng ( double v )
			=> double.IsFinite (v) && v >= 0.0 && v <= 100.0;

		private static bool IsValidDxy ( double v )
			=> double.IsFinite (v) && v > 0.0;

		private async Task UpdateFngAsync ( DateTime startUtc, DateTime endUtc, FillMode fillMode )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:fng] startUtc must be UTC. Got Kind={startUtc.Kind}.");
			if (endUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:fng] endUtc must be UTC. Got Kind={endUtc.Kind}.");

			if (fillMode != FillMode.Strict)
				throw new InvalidOperationException ("[indicators:fng] NeutralFill is not supported for FNG.");

			var start = startUtc.ToCausalDateUtc ();
			var end = endUtc.ToCausalDateUtc ();
			if (end < start)
				throw new InvalidOperationException ($"[indicators:fng] invalid range: start={start:O} end={end:O}");

			var storeFirst = _fngStore.TryGetFirstDate ();
			var storeLast = _fngStore.TryGetLastDate ();

			var targetEnd = storeLast.HasValue && storeLast.Value > end ? storeLast.Value : end;

			bool needRebuild = !storeFirst.HasValue || storeFirst.Value > start;

			// Источник FNG обычно отдаёт всю историю; при rebuild строим файл заново, иначе — дописываем хвост.
			var freshRaw = await DataLoading.GetFngHistory (_http);

			var fresh = new Dictionary<DateTime, double> (freshRaw.Count);
			foreach (var kv in freshRaw)
				fresh[kv.Key.ToCausalDateUtc ()] = kv.Value;

			DateTime from;
			if (needRebuild)
				{
				from = start;
				Console.WriteLine (
					$"[indicators:fng] rebuild: path='{_fngPath}', storeFirst={(storeFirst.HasValue ? storeFirst.Value.ToString ("yyyy-MM-dd") : "null")}, " +
					$"storeLast={(storeLast.HasValue ? storeLast.Value.ToString ("yyyy-MM-dd") : "null")}, start={start:yyyy-MM-dd}, end={targetEnd:yyyy-MM-dd}");
				}
			else
				{
				from = (storeLast?.AddDays (1) ?? start).ToCausalDateUtc ();
				if (from > targetEnd) return;

				Console.WriteLine (
					$"[indicators:fng] append: path='{_fngPath}', storeLast={(storeLast.HasValue ? storeLast.Value.ToString ("yyyy-MM-dd") : "null")}, " +
					$"from={from:yyyy-MM-dd}, end={targetEnd:yyyy-MM-dd}");
				}

			var lines = new List<IndicatorsNdjsonStore.IndicatorLine> ();
			var missingHard = new List<DateTime> ();

			for (var d = from; d <= targetEnd; d = d.AddDays (1))
				{
				if (!fresh.TryGetValue (d, out var fng) || !IsValidFng (fng))
					{
					missingHard.Add (d);
					continue;
					}

				lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, fng));
				}

			if (missingHard.Count > 0)
				{
				throw new InvalidOperationException (
					"[indicators:fng] missing/invalid days: " +
					string.Join (", ", missingHard.Take (200).Select (x => x.ToString ("yyyy-MM-dd"))) +
					(missingHard.Count > 200 ? $" ... (total={missingHard.Count})" : ""));
				}

			if (lines.Count == 0) return;

			if (needRebuild)
				{
				// Перезапись — единственный корректный способ backfill'а для NDJSON.
				_fngStore.OverwriteAtomic (lines);
				Console.WriteLine ($"[indicators] FNG rebuilt {lines.Count} days ({from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd})");
				}
			else
				{
				_fngStore.Append (lines);
				Console.WriteLine ($"[indicators] FNG appended {lines.Count} days ({from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd})");
				}
			}

		private async Task UpdateDxyAsync ( DateTime startUtc, DateTime endUtc, FillMode fillMode )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:dxy] startUtc must be UTC. Got Kind={startUtc.Kind}.");
			if (endUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:dxy] endUtc must be UTC. Got Kind={endUtc.Kind}.");

			var start = startUtc.ToCausalDateUtc ();
			var end = endUtc.ToCausalDateUtc ();
			if (end < start)
				throw new InvalidOperationException ($"[indicators:dxy] invalid range: start={start:O} end={end:O}");

			var storeFirst = _dxyStore.TryGetFirstDate ();
			var storeLast = _dxyStore.TryGetLastDate ();

			var targetEnd = storeLast.HasValue && storeLast.Value > end ? storeLast.Value : end;

			bool needRebuild = !storeFirst.HasValue || storeFirst.Value > start;

			DateTime from;
			if (needRebuild)
				{
				from = start;

				Console.WriteLine (
					$"[indicators:dxy] rebuild: path='{_dxyPath}', mode={fillMode}, storeFirst={(storeFirst.HasValue ? storeFirst.Value.ToString ("yyyy-MM-dd") : "null")}, " +
					$"storeLast={(storeLast.HasValue ? storeLast.Value.ToString ("yyyy-MM-dd") : "null")}, start={start:yyyy-MM-dd}, end={targetEnd:yyyy-MM-dd}");
				}
			else
				{
				from = (storeLast?.AddDays (1) ?? start).ToCausalDateUtc ();
				if (from > targetEnd) return;

				Console.WriteLine (
					$"[indicators:dxy] append: path='{_dxyPath}', mode={fillMode}, storeLast={(storeLast.HasValue ? storeLast.Value.ToString ("yyyy-MM-dd") : "null")}, " +
					$"from={from:yyyy-MM-dd}, end={targetEnd:yyyy-MM-dd}");
				}

			// Для NeutralFill нужен буфер назад, чтобы иметь lastKnown до первой дыры.
			var fetchFrom = (fillMode == FillMode.NeutralFill)
				? from.AddDays (-DxyBootstrapLookbackDays)
				: from;

			// Источник должен уметь отдавать глубокую историю. Если не умеет — упадём явно, без заглушек.
			var fetchedRaw = await DataLoading.GetDxySeries (_http, fetchFrom, targetEnd);

			var fetched = new Dictionary<DateTime, double> (fetchedRaw.Count);
			foreach (var kv in fetchedRaw)
				fetched[kv.Key.ToCausalDateUtc ()] = kv.Value;

			if (fetched.Count > 0)
				{
				var minK = fetched.Keys.Min ();
				var maxK = fetched.Keys.Max ();
				Console.WriteLine ($"[indicators:dxy] fetched: points={fetched.Count}, range=[{minK:yyyy-MM-dd}..{maxK:yyyy-MM-dd}]");
				}
			else
				{
				Console.WriteLine ("[indicators:dxy] fetched: points=0");
				}

			double? lastKnown = null;
			DateTime? lastKnownFactDate = null;

			// При append можно опираться на стор (строго, без "угадываний").
			if (!needRebuild && storeLast.HasValue && fillMode == FillMode.NeutralFill)
				{
				var lastKey = storeLast.Value.ToCausalDateUtc ();
				var lastMap = _dxyStore.ReadRange (lastKey, lastKey);

				if (!lastMap.TryGetValue (lastKey, out var lastVal))
					throw new InvalidOperationException ($"[indicators:dxy] store last date={lastKey:yyyy-MM-dd} is not readable as value.");

				if (!IsValidDxy (lastVal))
					throw new InvalidOperationException ($"[indicators:dxy] store last value is invalid: DXY@{lastKey:yyyy-MM-dd}={lastVal}.");

				lastKnown = lastVal;
				lastKnownFactDate = lastKey;
				}

			// При rebuild, если первый день "дырявый", разрешаем bootstrap из fetched <= (from-1).
			if (fillMode == FillMode.NeutralFill && !lastKnown.HasValue)
				{
				var probeEnd = from.AddDays (-1);

				DateTime? bestDate = null;
				double bestVal = default;

				foreach (var kv in fetched)
					{
					if (kv.Key > probeEnd) continue;
					if (!IsValidDxy (kv.Value)) continue;

					if (!bestDate.HasValue || kv.Key > bestDate.Value)
						{
						bestDate = kv.Key;
						bestVal = kv.Value;
						}
					}

				if (bestDate.HasValue)
					{
					lastKnown = bestVal;
					lastKnownFactDate = bestDate.Value;
					Console.WriteLine ($"[indicators:dxy] bootstrap lastKnown: date={bestDate:yyyy-MM-dd}, value={bestVal:G17}");
					}
				}

			var lines = new List<IndicatorsNdjsonStore.IndicatorLine> ();
			var missingHard = new List<DateTime> ();

			for (var d = from; d <= targetEnd; d = d.AddDays (1))
				{
				if (fetched.TryGetValue (d, out var v) && IsValidDxy (v))
					{
					lastKnown = v;
					lastKnownFactDate = d;
					lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, v));
					continue;
					}

				if (fillMode == FillMode.NeutralFill)
					{
					if (!lastKnown.HasValue)
						{
						missingHard.Add (d);
						continue;
						}

					lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, lastKnown.Value));
					continue;
					}

				missingHard.Add (d);
				}

			if (missingHard.Count > 0)
				{
				Console.WriteLine (
					$"[indicators:dxy] FAIL: missingHard={missingHard.Count}, first={missingHard[0]:yyyy-MM-dd}, " +
					$"lastKnownFactDate={(lastKnownFactDate.HasValue ? lastKnownFactDate.Value.ToString ("yyyy-MM-dd") : "null")}");

				throw new InvalidOperationException (
					"[indicators:dxy] missing/invalid days: " +
					string.Join (", ", missingHard.Take (200).Select (x => x.ToString ("yyyy-MM-dd"))) +
					(missingHard.Count > 200 ? $" ... (total={missingHard.Count})" : ""));
				}

			if (lines.Count == 0) return;

			if (needRebuild)
				{
				_dxyStore.OverwriteAtomic (lines);
				Console.WriteLine ($"[indicators] DXY rebuilt {lines.Count} days ({from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd})");
				}
			else
				{
				_dxyStore.Append (lines);
				Console.WriteLine ($"[indicators] DXY appended {lines.Count} days ({from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd})");
				}
			}
		}
	}
