using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Indicators
	{
	/// <summary>
	/// Индикаторы и утилиты по 6h-свечам и внешним дневным рядам (FNG/DXY).
	///
	/// Концептуально два класса контрактов:
	/// - warm-up: верхний уровень может получать NaN/пустые словари как маркер "ещё рано считать";
	/// - строгие методы: рассчитаны на вызовы после coverage-guard'ов и валидируют инварианты через исключения.
	/// </summary>
	public static class Indicators
		{
		// =========================
		// БАЗОВЫЕ УТИЛИТЫ
		// =========================

		/// <summary>
		/// Доходность за N окон назад: close[idx] / close[idx-N] - 1.
		/// NaN используется как маркер warm-up/недостаточной истории.
		/// </summary>
		public static double Ret6h ( IReadOnlyList<Candle6h> arr, int idx, int windowsBack )
			{
			if (arr == null || arr.Count == 0) return double.NaN;
			if (idx < 0 || idx >= arr.Count) return double.NaN;

			int prev = idx - windowsBack;
			if (prev < 0) return double.NaN;

			double now = arr[idx].Close;
			double past = arr[prev].Close;

			if (!double.IsFinite (now) || !double.IsFinite (past) || now <= 0.0 || past <= 0.0)
				return double.NaN;

			return now / past - 1.0;
			}

		/// <summary>
		/// Tolerant-lookup: точное значение на atUtc, иначе ближайшее предыдущее с шагом step до maxBackSteps.
		/// Возвращает defaultValue при отсутствии значения в lookback.
		/// </summary>
		public static double FindNearest ( Dictionary<DateTime, double> map, DateTime atUtc, double defaultValue, int maxBackSteps = 28, TimeSpan? step = null )
			{
			if (map == null || map.Count == 0) return defaultValue;
			if (map.TryGetValue (atUtc, out var exact)) return exact;

			var s = step ?? TimeSpan.FromHours (6);
			var d = atUtc;

			for (int i = 1; i <= maxBackSteps; i++)
				{
				d -= s;
				if (map.TryGetValue (d, out var v)) return v;
				}

			return defaultValue;
			}

		/// <summary>
		/// Строгий lookup: отсутствие значения в lookback считается нарушением инварианта.
		/// Сообщение об ошибке содержит диапазон доступных ключей и параметры поиска.
		/// </summary>
		public static double FindNearestOrThrow (
			Dictionary<DateTime, double> map,
			DateTime atUtc,
			string seriesKey,
			int maxBackSteps = 28,
			TimeSpan? step = null )
			{
			if (map == null || map.Count == 0)
				throw new InvalidOperationException ($"[indicators] {seriesKey} series is null or empty.");

			if (atUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators] {seriesKey} lookup time must be UTC. Got Kind={atUtc.Kind}.");

			if (map.TryGetValue (atUtc, out var exact))
				return exact;

			var s = step ?? TimeSpan.FromHours (6);
			if (s <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException (nameof (step), $"[indicators] {seriesKey} step must be > 0. Got {s}.");

			var min = map.Keys.Min ();
			var max = map.Keys.Max ();

			var d = atUtc;
			for (int i = 1; i <= maxBackSteps; i++)
				{
				d -= s;
				if (map.TryGetValue (d, out var v))
					return v;
				}

			throw new InvalidOperationException (
				$"[indicators] {seriesKey} value not found for {atUtc:O} " +
				$"(lookbackSteps={maxBackSteps}, step={s}). availableRange=[{min:O}..{max:O}].");
			}

		/// <summary>
		/// Средний |ret| за lookbackWindows окон, где ret=close/prevClose-1.
		/// Требует наличия prevClose для первого элемента окна (from >= 1).
		/// </summary>
		public static double ComputeDynVol6h ( IReadOnlyList<Candle6h> arr, int idx, int lookbackWindows )
			{
			if (arr == null) throw new ArgumentNullException (nameof (arr));
			if (arr.Count == 0) throw new ArgumentException ("arr must be non-empty.", nameof (arr));
			if (idx < 0 || idx >= arr.Count) throw new ArgumentOutOfRangeException (nameof (idx), $"idx={idx}, count={arr.Count}");
			if (lookbackWindows <= 0) throw new ArgumentOutOfRangeException (nameof (lookbackWindows), $"lookbackWindows={lookbackWindows}");

			int from = idx - lookbackWindows + 1;
			if (from < 1)
				throw new InvalidOperationException ($"[indicators] dynVol warm-up: idx={idx}, lookback={lookbackWindows}.");

			double sumAbs = 0.0;

			for (int i = from; i <= idx; i++)
				{
				double prev = arr[i - 1].Close;
				double cur = arr[i].Close;

				if (!double.IsFinite (prev) || !double.IsFinite (cur) || prev <= 0.0 || cur <= 0.0)
					{
					throw new InvalidOperationException (
						$"[indicators] dynVol invalid Close: i={i}, prevClose={prev}, curClose={cur}, tPrev={arr[i - 1].OpenTimeUtc:O}, tCur={arr[i].OpenTimeUtc:O}.");
					}

				sumAbs += Math.Abs (cur / prev - 1.0);
				}

			double dynVol = sumAbs / lookbackWindows;

			if (!double.IsFinite (dynVol))
				throw new InvalidOperationException ($"[indicators] dynVol non-finite after calc: {dynVol}. idx={idx}, lookback={lookbackWindows}.");

			return dynVol;
			}

		// =========================
		// ATR (Wilder)
		// =========================

		public static Dictionary<DateTime, double> ComputeAtr6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;

			Validate6hSeriesOrThrow (arr, "ATR");

			double[] tr = new double[arr.Count];

			tr[0] = arr[0].High - arr[0].Low;
			if (!double.IsFinite (tr[0]) || tr[0] < 0.0)
				throw new InvalidOperationException ($"[indicators:atr] invalid TR at i=0: {tr[0]} (h={arr[0].High}, l={arr[0].Low}, t={arr[0].OpenTimeUtc:O}).");

			for (int i = 1; i < arr.Count; i++)
				{
				double h = arr[i].High;
				double l = arr[i].Low;
				double pc = arr[i - 1].Close;

				double tri = Math.Max (h - l, Math.Max (Math.Abs (h - pc), Math.Abs (l - pc)));
				if (!double.IsFinite (tri) || tri < 0.0)
					throw new InvalidOperationException ($"[indicators:atr] invalid TR at i={i}: {tri} (t={arr[i].OpenTimeUtc:O}).");

				tr[i] = tri;
				}

			if (arr.Count < period) return res;

			double atr = tr.Take (period).Sum () / period;
			if (!double.IsFinite (atr) || atr <= 0.0)
				throw new InvalidOperationException ($"[indicators:atr] invalid initial ATR: {atr} (period={period}).");

			res[arr[period - 1].OpenTimeUtc] = atr;

			for (int i = period; i < arr.Count; i++)
				{
				atr = (atr * (period - 1) + tr[i]) / period;

				if (!double.IsFinite (atr) || atr <= 0.0)
					throw new InvalidOperationException ($"[indicators:atr] invalid ATR at i={i}: {atr} (t={arr[i].OpenTimeUtc:O}).");

				res[arr[i].OpenTimeUtc] = atr;
				}

			return res;
			}

		// =========================
		// RSI (Wilder)
		// =========================

		public static Dictionary<DateTime, double> ComputeRsi6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;

			Validate6hSeriesOrThrow (arr, "RSI");

			double[] closes = arr.Select (c => c.Close).ToArray ();
			if (closes.Length < period + 1) return res;

			double gain = 0.0, loss = 0.0;
			for (int i = 1; i <= period; i++)
				{
				double diff = closes[i] - closes[i - 1];
				if (diff >= 0) gain += diff; else loss -= diff;
				}

			double avgGain = gain / period;
			double avgLoss = loss / period;

			var r0 = ToRsi (avgGain, avgLoss);
			ValidateRsiOrThrow (r0, arr[period].OpenTimeUtc, "rsi:init");
			res[arr[period].OpenTimeUtc] = r0;

			for (int i = period + 1; i < closes.Length; i++)
				{
				double diff = closes[i] - closes[i - 1];
				double g = diff > 0 ? diff : 0.0;
				double l = diff < 0 ? -diff : 0.0;

				avgGain = (avgGain * (period - 1) + g) / period;
				avgLoss = (avgLoss * (period - 1) + l) / period;

				var r = ToRsi (avgGain, avgLoss);
				ValidateRsiOrThrow (r, arr[i].OpenTimeUtc, "rsi:step");
				res[arr[i].OpenTimeUtc] = r;
				}

			return res;

			static double ToRsi ( double ag, double al )
				{
				if (al == 0.0) return ag == 0.0 ? 50.0 : 100.0;
				double rs = ag / al;
				return 100.0 - 100.0 / (1.0 + rs);
				}
			}

		/// <summary>
		/// Наклон RSI между openUtc и openUtc-6h*steps.
		/// Подходит только для рядов, где ключи реально лежат на полной 6h-сетке без пропусков.
		/// </summary>
		public static double GetRsiSlope6h ( Dictionary<DateTime, double> rsiByOpenUtc, DateTime openUtc, int steps )
			{
			if (rsiByOpenUtc == null) throw new ArgumentNullException (nameof (rsiByOpenUtc));

			if (openUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:rsi] openUtc must be UTC. Got Kind={openUtc.Kind}, t={openUtc:O}.");

			if (steps <= 0)
				throw new ArgumentOutOfRangeException (nameof (steps), steps, "[indicators:rsi] steps must be > 0.");

			if (rsiByOpenUtc.Count == 0)
				throw new InvalidOperationException ("[indicators:rsi] RSI series is empty.");

			var fromUtc = openUtc.AddHours (-6 * steps);

			if (!rsiByOpenUtc.TryGetValue (openUtc, out var rsiNow))
				{
				var (min, max) = GetRange (rsiByOpenUtc);
				throw new InvalidOperationException (
					$"[indicators:rsi] RSI value missing at {openUtc:O} (steps={steps}). availableRange=[{min:O}..{max:O}], points={rsiByOpenUtc.Count}.");
				}

			if (!rsiByOpenUtc.TryGetValue (fromUtc, out var rsiFrom))
				{
				var (min, max) = GetRange (rsiByOpenUtc);
				throw new InvalidOperationException (
					$"[indicators:rsi] RSI slope source value missing at {fromUtc:O} for target {openUtc:O} (steps={steps}). availableRange=[{min:O}..{max:O}], points={rsiByOpenUtc.Count}.");
				}

			if (!double.IsFinite (rsiNow) || !double.IsFinite (rsiFrom))
				throw new InvalidOperationException (
					$"[indicators:rsi] Non-finite RSI values: now={rsiNow} at {openUtc:O}, from={rsiFrom} at {fromUtc:O}.");

			if (rsiNow < 0.0 || rsiNow > 100.0 || rsiFrom < 0.0 || rsiFrom > 100.0)
				throw new InvalidOperationException (
					$"[indicators:rsi] RSI out of range: now={rsiNow} at {openUtc:O}, from={rsiFrom} at {fromUtc:O}.");

			return (rsiNow - rsiFrom) / steps;
			}

		/// <summary>
		/// RSI-slope по "барной" сетке: сравниваем RSI на текущем баре idx и на баре (idx-steps).
		///
		/// Зачем это нужно:
		/// - если входной 6h-ряд очищен от выходных/праздников, то time-based offset (openUtc-6h*steps)
		///   может попадать в timestamp, которого в словаре RSI нет, хотя "предыдущие бары" реально существуют.
		///
		/// Контракт:
		/// - при idx < steps возвращаем NaN как маркер warm-up (верхний уровень сам решает skip/guard);
		/// - при отсутствии ключей RSI для idx или (idx-steps) — fail-fast с диагностикой (это уже дыра/рассинхрон).
		/// </summary>
		public static double GetRsiSlopeByBars (
			Dictionary<DateTime, double> rsiByOpenUtc,
			IReadOnlyList<Candle6h> arr,
			int idx,
			int steps,
			string seriesKey )
			{
			if (rsiByOpenUtc == null) throw new ArgumentNullException (nameof (rsiByOpenUtc));
			if (arr == null) throw new ArgumentNullException (nameof (arr));
			if (arr.Count == 0) throw new ArgumentException ("arr must be non-empty.", nameof (arr));

			if (idx < 0 || idx >= arr.Count)
				throw new ArgumentOutOfRangeException (nameof (idx), idx, $"idx={idx}, count={arr.Count}");

			if (steps <= 0)
				throw new ArgumentOutOfRangeException (nameof (steps), steps, "[indicators:rsi] steps must be > 0.");

			int requiredIdx = idx - steps;
			if (requiredIdx < 0)
				return double.NaN;

			var openUtc = arr[idx].OpenTimeUtc;
			var requiredFromUtc = arr[requiredIdx].OpenTimeUtc;

			if (openUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:rsi] openUtc must be UTC. Got Kind={openUtc.Kind}, t={openUtc:O}.");

			if (requiredFromUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators:rsi] requiredFromUtc must be UTC. Got Kind={requiredFromUtc.Kind}, t={requiredFromUtc:O}.");

			if (!rsiByOpenUtc.TryGetValue (openUtc, out var rsiNow))
				{
				var diag = IndicatorSeriesDiagnostics.DescribeMissingKey (
					series: rsiByOpenUtc,
					seriesKey: seriesKey,
					requiredUtc: openUtc,
					neighbors: 10);

				throw new InvalidOperationException (
					$"[indicators:rsi] {seriesKey} missing at {openUtc:O} (idx={idx}, steps={steps}, requiredIdx={requiredIdx}). {diag}");
				}

			if (!rsiByOpenUtc.TryGetValue (requiredFromUtc, out var rsiPrev))
				{
				var diag = IndicatorSeriesDiagnostics.DescribeMissingKey (
					series: rsiByOpenUtc,
					seriesKey: seriesKey,
					requiredUtc: requiredFromUtc,
					neighbors: 10);

				throw new InvalidOperationException (
					$"[indicators:rsi] {seriesKey} slope precondition failed at {openUtc:O}: " +
					$"missing RSI at {requiredFromUtc:O} for steps={steps} (idx={idx}, requiredIdx={requiredIdx}). {diag}");
				}

			if (!double.IsFinite (rsiNow) || !double.IsFinite (rsiPrev))
				throw new InvalidOperationException (
					$"[indicators:rsi] Non-finite RSI values: now={rsiNow} at {openUtc:O}, prev={rsiPrev} at {requiredFromUtc:O} (idx={idx}, steps={steps}).");

			if (rsiNow < 0.0 || rsiNow > 100.0 || rsiPrev < 0.0 || rsiPrev > 100.0)
				throw new InvalidOperationException (
					$"[indicators:rsi] RSI out of range: now={rsiNow} at {openUtc:O}, prev={rsiPrev} at {requiredFromUtc:O} (idx={idx}, steps={steps}).");

			return (rsiNow - rsiPrev) / steps;
			}

		// =========================
		// SMA / EMA
		// =========================

		public static Dictionary<DateTime, double> ComputeSma6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;

			Validate6hSeriesOrThrow (arr, "SMA");

			double sum = 0.0;
			Queue<double> q = new Queue<double> (period);

			for (int i = 0; i < arr.Count; i++)
				{
				double v = arr[i].Close;
				if (!double.IsFinite (v) || v <= 0.0)
					throw new InvalidOperationException ($"[indicators:sma] invalid Close at i={i}: {v} (t={arr[i].OpenTimeUtc:O}).");

				sum += v;
				q.Enqueue (v);

				if (q.Count > period) sum -= q.Dequeue ();

				if (q.Count == period)
					{
					var sma = sum / period;
					if (!double.IsFinite (sma) || sma <= 0.0)
						throw new InvalidOperationException ($"[indicators:sma] invalid SMA at i={i}: {sma} (period={period}).");

					res[arr[i].OpenTimeUtc] = sma;
					}
				}

			return res;
			}

		public static Dictionary<DateTime, double> ComputeEma6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;
			if (arr.Count < period) return res;

			Validate6hSeriesOrThrow (arr, "EMA");

			double sma = 0.0;
			for (int i = 0; i < period; i++)
				{
				double v = arr[i].Close;
				if (!double.IsFinite (v) || v <= 0.0)
					throw new InvalidOperationException ($"[indicators:ema] invalid Close in init window at i={i}: {v} (t={arr[i].OpenTimeUtc:O}).");
				sma += v;
				}
			sma /= period;

			double k = 2.0 / (period + 1);
			double ema = sma;

			if (!double.IsFinite (ema) || ema <= 0.0)
				throw new InvalidOperationException ($"[indicators:ema] invalid initial EMA: {ema} (period={period}).");

			res[arr[period - 1].OpenTimeUtc] = ema;

			for (int i = period; i < arr.Count; i++)
				{
				double close = arr[i].Close;
				if (!double.IsFinite (close) || close <= 0.0)
					throw new InvalidOperationException ($"[indicators:ema] invalid Close at i={i}: {close} (t={arr[i].OpenTimeUtc:O}).");

				ema = close * k + ema * (1.0 - k);

				if (!double.IsFinite (ema) || ema <= 0.0)
					throw new InvalidOperationException ($"[indicators:ema] invalid EMA at i={i}: {ema} (t={arr[i].OpenTimeUtc:O}).");

				res[arr[i].OpenTimeUtc] = ema;
				}

			return res;
			}

		// =========================
		// FNG / DXY (дневные ряды)
		// =========================

		public static double PickNearestFng ( Dictionary<DateTime, double> fngByDate, DateTime asOfUtcDate )
			{
			if (fngByDate == null || fngByDate.Count == 0)
				throw new InvalidOperationException ("[indicators] FNG series is null or empty.");

			if (asOfUtcDate.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators] asOfUtcDate must be UTC. Got Kind={asOfUtcDate.Kind}.");

			var asOfDayUtc = DateTime.SpecifyKind (asOfUtcDate.Date, DateTimeKind.Utc);

			if (fngByDate.TryGetValue (asOfDayUtc, out double v))
				{
				ValidateFngOrThrow (v, asOfDayUtc);
				return v;
				}

			for (int i = 1; i <= 14; i++)
				{
				var d = asOfDayUtc.AddDays (-i);
				if (fngByDate.TryGetValue (d, out v))
					{
					ValidateFngOrThrow (v, d);
					return v;
					}
				}

			throw new InvalidOperationException ($"[indicators] FNG value not found for {asOfDayUtc:O} (lookback=14d).");
			}

		public static bool TryPickNearestFng ( Dictionary<DateTime, double> fngByDate, DateTime asOfUtcDate, out double value )
			{
			value = default;

			if (fngByDate == null || fngByDate.Count == 0) return false;
			if (asOfUtcDate.Kind != DateTimeKind.Utc) return false;

			var asOfDayUtc = DateTime.SpecifyKind (asOfUtcDate.Date, DateTimeKind.Utc);

			if (fngByDate.TryGetValue (asOfDayUtc, out value))
				return IsValidFng (value);

			for (int i = 1; i <= 14; i++)
				{
				var d = asOfDayUtc.AddDays (-i);
				if (fngByDate.TryGetValue (d, out value))
					return IsValidFng (value);
				}

			value = default;
			return false;
			}

		public static double GetDxyChange30 ( Dictionary<DateTime, double> dxySeries, DateTime asOfUtcDate )
			{
			if (dxySeries == null || dxySeries.Count == 0)
				throw new InvalidOperationException ("[indicators] DXY series is null or empty.");

			if (asOfUtcDate.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[indicators] asOfUtcDate must be UTC. Got Kind={asOfUtcDate.Kind}.");

			var asOfDayUtc = DateTime.SpecifyKind (asOfUtcDate.Date, DateTimeKind.Utc);

			double now = FindNearestOrThrow (
				dxySeries,
				asOfDayUtc,
				seriesKey: "DXY",
				maxBackSteps: 40,
				step: TimeSpan.FromDays (1));

			if (!double.IsFinite (now) || now <= 0.0)
				throw new InvalidOperationException ($"[indicators] DXY 'now' invalid for {asOfDayUtc:O}: {now}.");

			var pastDayUtc = DateTime.SpecifyKind (asOfDayUtc.AddDays (-30).Date, DateTimeKind.Utc);

			double past = FindNearestOrThrow (
				dxySeries,
				pastDayUtc,
				seriesKey: "DXY",
				maxBackSteps: 45,
				step: TimeSpan.FromDays (1));

			if (!double.IsFinite (past) || past <= 0.0)
				throw new InvalidOperationException ($"[indicators] DXY 'past' invalid for {pastDayUtc:O}: {past}.");

			return now / past - 1.0;
			}

		public static bool TryGetDxyChange30 ( Dictionary<DateTime, double> dxySeries, DateTime asOfUtcDate, out double change30 )
			{
			change30 = default;

			if (dxySeries == null || dxySeries.Count == 0) return false;
			if (asOfUtcDate.Kind != DateTimeKind.Utc) return false;

			var asOfDayUtc = DateTime.SpecifyKind (asOfUtcDate.Date, DateTimeKind.Utc);

			double now = FindNearest (dxySeries, asOfDayUtc, double.NaN, maxBackSteps: 40, step: TimeSpan.FromDays (1));
			if (!double.IsFinite (now) || now <= 0.0) return false;

			var pastDayUtc = DateTime.SpecifyKind (asOfDayUtc.AddDays (-30).Date, DateTimeKind.Utc);
			double past = FindNearest (dxySeries, pastDayUtc, double.NaN, maxBackSteps: 45, step: TimeSpan.FromDays (1));
			if (!double.IsFinite (past) || past <= 0.0) return false;

			change30 = now / past - 1.0;
			return true;
			}

		// =========================
		// ВНУТРЕННИЕ GUARDS
		// =========================

		private static void Validate6hSeriesOrThrow ( IReadOnlyList<Candle6h> arr, string kind )
			{
			for (int i = 0; i < arr.Count; i++)
				{
				var t = arr[i].OpenTimeUtc;
				if (t.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[indicators:{kind}] OpenTimeUtc must be UTC at i={i}. Got Kind={t.Kind}, t={t:O}.");

				double c = arr[i].Close;
				double h = arr[i].High;
				double l = arr[i].Low;

				if (!double.IsFinite (c) || c <= 0.0)
					throw new InvalidOperationException ($"[indicators:{kind}] invalid Close at i={i}: {c} (t={t:O}).");

				if (!double.IsFinite (h) || !double.IsFinite (l) || h <= 0.0 || l <= 0.0 || h < l)
					throw new InvalidOperationException ($"[indicators:{kind}] invalid High/Low at i={i}: h={h}, l={l} (t={t:O}).");
				}
			}

		private static void ValidateRsiOrThrow ( double rsi, DateTime tUtc, string tag )
			{
			if (!double.IsFinite (rsi) || rsi < 0.0 || rsi > 100.0)
				throw new InvalidOperationException ($"[indicators:rsi] invalid RSI ({tag}) at {tUtc:O}: {rsi}.");
			}

		private static bool IsValidFng ( double v )
			=> double.IsFinite (v) && v >= 0.0 && v <= 100.0;

		private static void ValidateFngOrThrow ( double v, DateTime dUtc )
			{
			if (!IsValidFng (v))
				throw new InvalidOperationException ($"[indicators] FNG invalid at {dUtc:O}: {v} (expected 0..100).");
			}

		private static (DateTime Min, DateTime Max) GetRange ( Dictionary<DateTime, double> map )
			{
			var min = map.Keys.Min ();
			var max = map.Keys.Max ();
			return (min, max);
			}
		}
	}
