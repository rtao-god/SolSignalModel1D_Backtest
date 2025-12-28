using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Indicators
	{
	/// <summary>
	/// Обновляет дневные индикаторы (FNG, DXY) в формате NDJSON.
	///
	/// Зачем вообще отдельный апдейтер:
	/// - индикаторы живут в своём кэше и потребляются многими частями пайплайна;
	/// - ошибки покрытия/валидности нужно ловить здесь, чтобы не размазывать проверки по ML/Labeling.
	///
	/// Модель данных:
	/// - каждый день = отдельная строка NDJSON (атомарно перезаписываем при backfill, иначе дописываем хвост).
	///
	/// Важное разделение ответственности:
	/// - Update*Async: гарантирует, что стор хранит "какой-то" корректный ряд по правилам выбранного FillMode.
	/// - EnsureCoverageOrFail: валидирует контракты потребления (например, lookback для FNG).
	/// </summary>
	public sealed class IndicatorsDailyUpdater
		{
		/// <summary>
		/// Strict:
		/// - отсутствующие/невалидные дни считаются фатальной проблемой источника и ломают запуск.
		///
		/// NeutralFill:
		/// - допускает заполнение пропусков только переносом последнего валидного факта вперёд (carry-forward).
		/// - это не "псевдо-данные", а явная политика устойчивости к единичным дыркам источника.
		/// - каждый заполненный день обязательно журналируется в отдельный NDJSON, чтобы на фронте/в отчётах
		///   можно было показать: "в этот день индикатор был заполнен переносом, а не пришёл из источника".
		///
		/// Принципиально НЕ делаем:
		/// - интерполяцию, сглаживание, синтез значений (это уже модель, а не кэширование фактов).
		/// </summary>
		public enum FillMode
			{
			Strict = 0,
			NeutralFill = 1
			}

		// URL фиксируем здесь: в исключениях/логах должно быть очевидно, какой источник лежит за кэшем.
		private const string FngApiUrl = "https://api.alternative.me/fng/?limit=0";

		// Контракт потребления FNG в CoreIndicators.PickNearestFng:
		// ищем последнее известное значение <= asOf в пределах lookback N дней.
		// Поэтому "день-в-день" покрытие для FNG не обязательно, важна разрешимость запроса.
		private const int FngPickNearestLookbackDays = 14;

		private readonly HttpClient _http;
		private readonly IndicatorsNdjsonStore _fngStore;
		private readonly IndicatorsNdjsonStore _dxyStore;

		private readonly string _fngPath;
		private readonly string _dxyPath;

		// Для DXY в NeutralFill важно иметь "опорный факт" до первой потенциальной дыры.
		// Поэтому при fetch берём небольшой буфер назад, чтобы carry-forward мог стартовать корректно.
		private const int DxyBootstrapLookbackDays = 14;

		// Журнал заполнений:
		// - отдельные файлы, чтобы не смешивать "ряд значений" и "метаданные о том, как он получен".
		// - NDJSON для удобства: можно стримить, грепать, парсить построчно.
		private readonly string _fillsDir;
		private readonly string _fngFillsPath;
		private readonly string _dxyFillsPath;

		public IndicatorsDailyUpdater ( HttpClient http )
			{
			_http = http ?? throw new ArgumentNullException (nameof (http));

			_fngPath = Path.Combine (PathConfig.IndicatorsDir, "fng.ndjson");
			_dxyPath = Path.Combine (PathConfig.IndicatorsDir, "dxy.ndjson");

			_fngStore = new IndicatorsNdjsonStore (_fngPath);
			_dxyStore = new IndicatorsNdjsonStore (_dxyPath);

			_fillsDir = Path.Combine (PathConfig.IndicatorsDir, "_fills");
			Directory.CreateDirectory (_fillsDir);

			_fngFillsPath = Path.Combine (_fillsDir, "fng.fills.ndjson");
			_dxyFillsPath = Path.Combine (_fillsDir, "dxy.fills.ndjson");
			}

		public async Task UpdateAllAsync (
			DateTime rangeStartUtc,
			DateTime rangeEndUtc,
			FillMode fngFillMode,
			FillMode dxyFillMode )
			{
			// Порядок не важен: сторы независимы.
			await UpdateFngAsync (rangeStartUtc, rangeEndUtc, fngFillMode);
			await UpdateDxyAsync (rangeStartUtc, rangeEndUtc, dxyFillMode);
			}

		public Task UpdateAllAsync ( DateTime rangeStartUtc, DateTime rangeEndUtc, FillMode fillMode )
			=> UpdateAllAsync (rangeStartUtc, rangeEndUtc, fngFillMode: fillMode, dxyFillMode: fillMode);

		/// <summary>
		/// Проверяет покрытие в терминах КОНТРАКТА ПОТРЕБЛЕНИЯ:
		/// - DXY: ожидаем дневное покрытие (NeutralFill должен закрывать выходные/праздники).
		/// - FNG: не требуем запись на каждый день; требуем, чтобы на любой день d был доступен lastKnown<=d
		///   в пределах lookback, иначе PickNearestFng становится неразрешим.
		///
		/// Если здесь падаем — это "ошибка данных/кэша", а не ошибка ML/feature-инжиниринга.
		/// </summary>
		public void EnsureCoverageOrFail ( DateTime rangeStartUtc, DateTime rangeEndUtc )
			{
			var start = rangeStartUtc.ToCausalDateUtc ();
			var end = rangeEndUtc.ToCausalDateUtc ();

			if (end < start)
				throw new InvalidOperationException ($"[indicators] ensure: invalid range start={start:O}, end={end:O}.");

			// Для проверки lookback по FNG читаем с буфером назад,
			// иначе в начале окна невозможно оценить "есть ли lastKnown в пределах N дней".
			var fngReadFrom = start.AddDays (-FngPickNearestLookbackDays);

			var fng = _fngStore.ReadRange (fngReadFrom, end);
			var dxy = _dxyStore.ReadRange (start, end);

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
				$"fngReadFrom={fngReadFrom:yyyy-MM-dd}, fng={Describe (fng)}, dxy={Describe (dxy)}");

			// 1) Валидность значений.
			// Важно: валидируем не только "внутри основного окна", потому что FNG может выбираться из буфера lookback.
			foreach (var kv in fng)
				{
				var d = kv.Key;
				var v = kv.Value;

				if (!double.IsFinite (v) || v < 0.0 || v > 100.0)
					invalid.Add ($"FNG@{d:yyyy-MM-dd}={v}");
				}

			foreach (var kv in dxy)
				{
				var d = kv.Key;
				var v = kv.Value;

				if (!double.IsFinite (v) || v <= 0.0)
					invalid.Add ($"DXY@{d:yyyy-MM-dd}={v}");
				}

			// 2) DXY: ожидаем дневное покрытие.
			// Если используем Strict, то источник обязан отдать каждый день; если NeutralFill — кэш обязан закрыть дырки переносом.
			for (var d = start; d <= end; d = d.AddDays (1))
				{
				if (!dxy.ContainsKey (d))
					missing.Add ($"DXY@{d:yyyy-MM-dd}");
				}

			// 3) FNG: проверяем разрешимость lastKnown<=asOf в пределах lookback.
			if (fng.Count == 0)
				{
				missing.Add ($"FNG@{start:yyyy-MM-dd}..{end:yyyy-MM-dd} (series empty)");
				}
			else
				{
				var fngKeys = fng.Keys.OrderBy (x => x).ToArray ();

				int idx = 0;
				DateTime? lastKnownDate = null;

				for (var d = start; d <= end; d = d.AddDays (1))
					{
					while (idx < fngKeys.Length && fngKeys[idx] <= d)
						{
						lastKnownDate = fngKeys[idx];
						idx++;
						}

					if (!lastKnownDate.HasValue)
						{
						missing.Add ($"FNG@{d:yyyy-MM-dd} (no <=asOf value, lookback={FngPickNearestLookbackDays}d)");
						continue;
						}

					var gapDays = (d - lastKnownDate.Value).TotalDays;
					if (gapDays > FngPickNearestLookbackDays)
						{
						missing.Add (
							$"FNG@{d:yyyy-MM-dd} (gap={gapDays:0}d, lastKnown={lastKnownDate.Value:yyyy-MM-dd}, lookback={FngPickNearestLookbackDays}d)");
						}
					}
				}

			static string FormatList ( List<string> xs, int take )
				{
				if (xs.Count == 0) return "none";
				return string.Join (", ", xs.Take (take)) + (xs.Count > take ? $" (+{xs.Count - take} more)" : "");
				}

			if (missing.Count > 0)
				{
				throw new InvalidOperationException (
					$"[indicators] missing/unresolvable: {FormatList (missing, 40)}. " +
					$"check=[{start:yyyy-MM-dd}..{end:yyyy-MM-dd}], fngReadFrom={fngReadFrom:yyyy-MM-dd}, " +
					$"fng={Describe (fng)}, dxy={Describe (dxy)}");
				}

			if (invalid.Count > 0)
				{
				throw new InvalidOperationException (
					$"[indicators] invalid values: {FormatList (invalid, 40)}. " +
					$"check=[{start:yyyy-MM-dd}..{end:yyyy-MM-dd}], fngReadFrom={fngReadFrom:yyyy-MM-dd}, " +
					$"fng={Describe (fng)}, dxy={Describe (dxy)}");
				}
			}

		/// <summary>
		/// FNG хранится как double, но по факту дискретный индекс 0..100.
		/// Здесь приводим к int, чтобы downstream не зависел от дробей (и чтобы случайные "99.999" не гуляли).
		/// </summary>
		public Dictionary<DateTime, double> LoadFngDict ( DateTime startUtc, DateTime endUtc )
			{
			var raw = _fngStore.ReadRange (startUtc, endUtc);
			if (LeakageSwitches.IsEnabled (LeakageMode.IndicatorsShiftForward1Day))
				raw = ShiftValuesForwardByDays (raw, days: 1);

			var res = new Dictionary<DateTime, double> (raw.Count);

			foreach (var kv in raw)
				res[kv.Key] = (int) Math.Round (kv.Value);

			return res;
			}

		public Dictionary<DateTime, double> LoadDxyDict ( DateTime startUtc, DateTime endUtc )
			{
			var raw = _dxyStore.ReadRange (startUtc, endUtc);
			if (LeakageSwitches.IsEnabled (LeakageMode.IndicatorsShiftForward1Day))
				raw = ShiftValuesForwardByDays (raw, days: 1);
			return raw;
			}

		private static Dictionary<DateTime, double> ShiftValuesForwardByDays ( Dictionary<DateTime, double> src, int days )
			{
			if (days <= 0) return src;
			var shifted = new Dictionary<DateTime, double> (src.Count);

			foreach (var kv in src)
				{
				var futureKey = kv.Key.AddDays (days);
				if (src.TryGetValue (futureKey, out var futureVal))
					shifted[kv.Key] = futureVal;
				else
					shifted[kv.Key] = kv.Value;
				}

			return shifted;
			}

		private static bool IsValidFng ( double v )
			=> double.IsFinite (v) && v >= 0.0 && v <= 100.0;

		private static bool IsValidDxy ( double v )
			=> double.IsFinite (v) && v > 0.0;

		/// <summary>
		/// Структура строки журнала заполнения.
		/// Инвариант: наличие такой строки означает, что значение на dayUtc НЕ пришло напрямую из источника,
		/// а было получено переносом последнего валидного факта.
		/// </summary>
		private sealed class FillLine
			{
			public string Kind { get; set; } = null!;
			public DateTime LoggedAtUtc { get; set; }
			public string Indicator { get; set; } = null!;
			public string Mode { get; set; } = null!;
			public DateTime DayUtc { get; set; }
			public DateTime? CarriedFromDayUtc { get; set; }
			public double Value { get; set; }
			public string Reason { get; set; } = null!;
			}

		/// <summary>
		/// Аппенд в NDJSON намеренно максимально простой:
		/// - для этих файлов нет требований к атомарности (это аудит, а не источник истины);
		/// - append в конец даёт естественный chronological log.
		/// </summary>
		private void AppendFill (
			string path,
			string indicator,
			FillMode mode,
			DateTime dayUtc,
			DateTime? carriedFromDayUtc,
			double value,
			string reason )
			{
			var line = new FillLine
				{
				Kind = "indicator-fill",
				LoggedAtUtc = DateTime.UtcNow,
				Indicator = indicator,
				Mode = mode.ToString (),
				DayUtc = dayUtc,
				CarriedFromDayUtc = carriedFromDayUtc,
				Value = value,
				Reason = reason
				};

			var json = JsonSerializer.Serialize (line);
			File.AppendAllText (path, json + Environment.NewLine, Encoding.UTF8);
			}

		/// <summary>
		/// Обновление FNG.
		///
		/// Особенность источника:
		/// - обычно отдаёт большую (почти всю) историю;
		/// - иногда бывают одиночные дырки/битые дни (как 2024-10-26).
		///
		/// Политика:
		/// - Strict: упадём на любой дырке, чтобы не "замести под ковёр" проблему источника.
		/// - NeutralFill: переносим последний факт вперёд и журналируем каждый перенос.
		///
		/// Это решение стабилизирует пайплайн, но оставляет полный аудит того, где именно были переносы.
		/// </summary>
		private async Task UpdateFngAsync ( DateTime startUtc, DateTime endUtc, FillMode fillMode )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:fng] startUtc must be UTC. Got Kind={startUtc.Kind}.");
			if (endUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:fng] endUtc must be UTC. Got Kind={endUtc.Kind}.");

			var start = startUtc.ToCausalDateUtc ();
			var end = endUtc.ToCausalDateUtc ();
			if (end < start)
				throw new InvalidOperationException ($"[indicators:fng] invalid range: start={start:O} end={end:O}");

			var storeFirst = _fngStore.TryGetFirstDate ();
			var storeLast = _fngStore.TryGetLastDate ();

			var targetEnd = storeLast.HasValue && storeLast.Value > end ? storeLast.Value : end;

			bool needRebuild = !storeFirst.HasValue || storeFirst.Value > start;

			// Историю берём одним запросом.
			var freshRaw = await DataLoading.GetFngHistory (_http);

			// Ключи нормализуем в day-key (UTC midnight).
			var fresh = new Dictionary<DateTime, double> (freshRaw.Count);
			foreach (var kv in freshRaw)
				fresh[kv.Key.ToCausalDateUtc ()] = kv.Value;

			DateTime from;
			if (needRebuild)
				{
				from = start;
				Console.WriteLine (
					$"[indicators:fng] rebuild: path='{_fngPath}', mode={fillMode}, storeFirst={(storeFirst.HasValue ? storeFirst.Value.ToString ("yyyy-MM-dd") : "null")}, " +
					$"storeLast={(storeLast.HasValue ? storeLast.Value.ToString ("yyyy-MM-dd") : "null")}, start={start:yyyy-MM-dd}, end={targetEnd:yyyy-MM-dd}");
				}
			else
				{
				from = (storeLast?.AddDays (1) ?? start).ToCausalDateUtc ();
				if (from > targetEnd) return;

				Console.WriteLine (
					$"[indicators:fng] append: path='{_fngPath}', mode={fillMode}, storeLast={(storeLast.HasValue ? storeLast.Value.ToString ("yyyy-MM-dd") : "null")}, " +
					$"from={from:yyyy-MM-dd}, end={targetEnd:yyyy-MM-dd}");
				}

			double? lastKnown = null;
			DateTime? lastKnownFactDate = null;

			// Якорь для append+NeutralFill берём из стора.
			if (!needRebuild && storeLast.HasValue && fillMode == FillMode.NeutralFill)
				{
				var lastKey = storeLast.Value.ToCausalDateUtc ();
				var lastMap = _fngStore.ReadRange (lastKey, lastKey);

				if (!lastMap.TryGetValue (lastKey, out var lastVal))
					throw new InvalidOperationException ($"[indicators:fng] store last date={lastKey:yyyy-MM-dd} is not readable as value.");

				if (!IsValidFng (lastVal))
					throw new InvalidOperationException ($"[indicators:fng] store last value is invalid: FNG@{lastKey:yyyy-MM-dd}={lastVal}.");

				lastKnown = lastVal;
				lastKnownFactDate = lastKey;
				}

			// Bootstrap для rebuild+NeutralFill: ищем факт <= (from-1), чтобы было чем carry-forward стартовать.
			if (fillMode == FillMode.NeutralFill && !lastKnown.HasValue)
				{
				var probeEnd = from.AddDays (-1);

				DateTime? bestDate = null;
				double bestVal = default;

				foreach (var kv in fresh)
					{
					if (kv.Key > probeEnd) continue;
					if (!IsValidFng (kv.Value)) continue;

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
					Console.WriteLine ($"[indicators:fng] bootstrap lastKnown: date={bestDate:yyyy-MM-dd}, value={bestVal:G17}");
					}
				}

			var lines = new List<IndicatorsNdjsonStore.IndicatorLine> ();
			var invalidHard = new List<string> (capacity: 32);
			var missingHard = new List<DateTime> (capacity: 32);
			var filledDays = new List<DateTime> (capacity: 32);

			for (var d = from; d <= targetEnd; d = d.AddDays (1))
				{
				// 1) Если источник отдал значение, но оно невалидно — это ошибка источника.
				if (fresh.TryGetValue (d, out var raw) && !IsValidFng (raw))
					invalidHard.Add ($"FNG@{d:yyyy-MM-dd}={raw}");

				// 2) Валидный факт из источника — всегда пишем и обновляем якорь.
				if (fresh.TryGetValue (d, out var fng) && IsValidFng (fng))
					{
					lastKnown = fng;
					lastKnownFactDate = d;
					lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, fng));
					continue;
					}

				// 3) Дальше — только политика заполнения дыр.
				if (fillMode == FillMode.NeutralFill)
					{
					if (!lastKnown.HasValue)
						{
						missingHard.Add (d);
						continue;
						}

					lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, lastKnown.Value));
					filledDays.Add (d);

					AppendFill (
						path: _fngFillsPath,
						indicator: "FNG",
						mode: fillMode,
						dayUtc: d,
						carriedFromDayUtc: lastKnownFactDate,
						value: lastKnown.Value,
						reason: "carry-forward last known valid fact (source missing/invalid)");

					continue;
					}

				// Strict: пропуск дня допустим (ряд “фактов” может быть разреженным).
				}

			// В Strict не даём “тихо” проглотить невалидные значения источника.
			if (fillMode == FillMode.Strict && invalidHard.Count > 0)
				{
				throw new InvalidOperationException (
					$"[indicators:fng] invalid source values: {string.Join (", ", invalidHard.Take (20))}" +
					(invalidHard.Count > 20 ? $" (+{invalidHard.Count - 20} more)" : "") +
					$". expectedRange={from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd}, cacheFile='{_fngPath}', apiUrl='{FngApiUrl}', fetchedDays={fresh.Count}, writtenDays={lines.Count}, mode={fillMode}");
				}

			if (missingHard.Count > 0)
				{
				// Это возможно только в NeutralFill (нет якоря для carry-forward).
				throw new InvalidOperationException (
					$"[indicators:fng] missing days with no bootstrap anchor: {string.Join (", ", missingHard.Take (20).Select (x => x.ToString ("yyyy-MM-dd")))}" +
					(missingHard.Count > 20 ? $" (+{missingHard.Count - 20} more)" : "") +
					$". expectedRange={from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd}, cacheFile='{_fngPath}', apiUrl='{FngApiUrl}', fetchedDays={fresh.Count}, writtenDays={lines.Count}, mode={fillMode}");
				}

			if (lines.Count == 0)
				throw new InvalidOperationException (
					$"[indicators:fng] no points written. expectedRange={from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd}, cacheFile='{_fngPath}', apiUrl='{FngApiUrl}', fetchedDays={fresh.Count}, mode={fillMode}");

			if (needRebuild)
				{
				_fngStore.OverwriteAtomic (lines);
				Console.WriteLine ($"[indicators] FNG rebuilt {lines.Count} days ({from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd}), filled={filledDays.Count}");
				}
			else
				{
				_fngStore.Append (lines);
				Console.WriteLine ($"[indicators] FNG appended {lines.Count} days ({from:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd}), filled={filledDays.Count}");
				}
			}

		/// <summary>
		/// Обновление DXY.
		///
		/// Отличие от FNG:
		/// - DXY часто отсутствует на выходные/праздники как рыночный индекс.
		/// - downstream обычно хочет ежедневное покрытие, поэтому NeutralFill здесь особенно полезен.
		///
		/// Правило NeutralFill:
		/// - переносим последний валидный факт вперёд (carry-forward),
		/// - каждое заполнение журналируем.
		/// </summary>
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

			// NeutralFill требует буфер назад: чтобы найти последний факт до первой дыры.
			var fetchFrom = (fillMode == FillMode.NeutralFill)
				? from.AddDays (-DxyBootstrapLookbackDays)
				: from;

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

			// Для append + NeutralFill берём якорь из стора (если он есть): это самый надёжный "факт".
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

			// Для rebuild + NeutralFill ищем лучший факт <= (from-1) в fetched.
			// Это нужно, чтобы carry-forward не начинался "с пустоты".
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
			var missingHard = new List<DateTime> (capacity: 32);

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

					AppendFill (
						path: _dxyFillsPath,
						indicator: "DXY",
						mode: fillMode,
						dayUtc: d,
						carriedFromDayUtc: lastKnownFactDate,
						value: lastKnown.Value,
						reason: "carry-forward last known valid fact (source missing/invalid)");

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
