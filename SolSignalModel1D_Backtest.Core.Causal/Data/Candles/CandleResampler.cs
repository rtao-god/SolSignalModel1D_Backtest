using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Domain;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles
	{
	/// <summary>
	/// Ресэмплер: собирает 6h из 1h или 1m NDJSON.
	/// Пишет в cache/candles/{SYMBOL}-6h.ndjson.
	/// Используется как резервный путь, если 6h-файл ещё не построен напрямую.
	/// </summary>
	public static class CandleResampler
		{
		private static string Path6h ( string symbol ) => CandlePaths.File (symbol, "6h");
		private static string Path1h ( string symbol ) => CandlePaths.File (symbol, "1h");
		private static string Path1m ( string symbol ) => CandlePaths.File (symbol, "1m");

		public static void Ensure6hAvailable ( string symbol )
			{
			Directory.CreateDirectory (PathConfig.CandlesDir);

			var p6 = Path6h (symbol);
			if (File.Exists (p6) && new FileInfo (p6).Length > 0)
				{
				Ensure6hUniformOrRebuild (symbol, p6);
				return;
				}

			var p1h = Path1h (symbol);
			if (File.Exists (p1h) && new FileInfo (p1h).Length > 0)
				{
				var oneHour = ReadAllLines (p1h);
				try
					{
					EnsureUniformStepUtc (oneHour, TimeSpan.FromHours (1), $"{symbol} 1h");
					var sixHour = ResampleTo6h (oneHour, sourceIsOneHour: true);
					WriteAll (p6, sixHour);
					return;
					}
				catch (InvalidOperationException ex)
					{
					Console.WriteLine (
						$"[resample] {symbol} 1h: найдены разрывы, переход на 1m. причина={ex.Message}");
					}
				}

			var oneMin = ReadAll1mMerged (symbol);
			if (oneMin.Count > 0)
				{
				EnsureUniformStepUtcAllowKnownGaps (oneMin, TimeSpan.FromMinutes (1), symbol, "1m", $"{symbol} 1m");
				var sixHour = ResampleTo6h (oneMin, sourceIsOneHour: false);
				WriteAll (p6, sixHour);
				return;
				}

			throw new InvalidOperationException (
				$"[resample] Нет ни 1h, ни 1m свечей для {symbol} в {PathConfig.CandlesDir}. " +
				$"Ожидались файлы: {Path.GetFileName (p1h)} или {Path.GetFileName (Path1m (symbol))}.");
			}

		private static void Ensure6hUniformOrRebuild ( string symbol, string path6h )
			{
			var sixHour = ReadAllLines (path6h);
			if (sixHour.Count == 0)
				{
				Rebuild6hFromBestSourceOrThrow (symbol, path6h, "6h file is empty");
				return;
				}

			EnsureSortedStrict (sixHour, $"{symbol} 6h");

			if (IsPaxgSymbol (symbol))
				{
				EnsureUniformStepUtcAllowPaxgWeekendGaps (sixHour, $"{symbol} 6h");
				return;
				}

			try
				{
				SeriesGuards.EnsureUniformStepUtc (sixHour, c => c.OpenTimeUtc, TimeSpan.FromHours (6), $"{symbol} 6h");
				return;
				}
			catch (InvalidOperationException ex)
				{
				Console.WriteLine (
					$"[resample] {symbol} 6h: найдены разрывы, пересборка из 1h/1m. причина={ex.Message}");

				Rebuild6hFromBestSourceOrThrow (symbol, path6h, ex.Message);
				}

			var rebuilt = ReadAllLines (path6h);
			if (rebuilt.Count == 0)
				throw new InvalidOperationException ($"[resample] {symbol} 6h rebuild produced empty series.");

			EnsureSortedStrict (rebuilt, $"{symbol} 6h (rebuilt)");
			SeriesGuards.EnsureUniformStepUtc (rebuilt, c => c.OpenTimeUtc, TimeSpan.FromHours (6), $"{symbol} 6h (rebuilt)");
			}

		private static void Rebuild6hFromBestSourceOrThrow ( string symbol, string path6h, string reason )
			{
			var p1h = Path1h (symbol);
			if (File.Exists (p1h) && new FileInfo (p1h).Length > 0)
				{
				var oneHour = ReadAllLines (p1h);
				try
					{
					EnsureUniformStepUtc (oneHour, TimeSpan.FromHours (1), $"{symbol} 1h");
					var sixHour = ResampleTo6h (oneHour, sourceIsOneHour: true);
					WriteAll (path6h, sixHour);

					Console.WriteLine (
						$"[resample] {symbol} 6h пересобран из 1h. " +
						$"кол-во={oneHour.Count}, причина={reason}");
					return;
					}
				catch (InvalidOperationException ex)
					{
					Console.WriteLine (
						$"[resample] {symbol} 1h: найдены разрывы, переход на 1m. причина={ex.Message}");
					}
				}

			var oneMin = ReadAll1mMerged (symbol);
			if (oneMin.Count > 0)
				{
				EnsureUniformStepUtcAllowKnownGaps (oneMin, TimeSpan.FromMinutes (1), symbol, "1m", $"{symbol} 1m");
				var sixHour = ResampleTo6h (oneMin, sourceIsOneHour: false);
				WriteAll (path6h, sixHour);

				Console.WriteLine (
					$"[resample] {symbol} 6h пересобран из 1m. " +
					$"кол-во={oneMin.Count}, причина={reason}");
				return;
				}

			throw new InvalidOperationException (
				$"[resample] Cannot rebuild 6h for {symbol}: no 1h/1m sources. reason={reason}");
			}

		private static List<CandleNdjsonStore.CandleLine> ReadAllLines ( string path )
			{
			var st = new CandleNdjsonStore (path);
			return st.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			}

		private static void WriteAll ( string path, IEnumerable<CandleNdjsonStore.CandleLine> lines )
			{
			if (File.Exists (path)) File.Delete (path);
			var store = new CandleNdjsonStore (path);
			store.Append (lines);
			}

		private static List<CandleNdjsonStore.CandleLine> ReadAll1mMerged ( string symbol )
			{
			var weekdayPath = Path1m (symbol);
			var weekendPath = CandlePaths.WeekendFile (symbol, "1m");

			var weekdays = File.Exists (weekdayPath) ? ReadAllLines (weekdayPath) : new List<CandleNdjsonStore.CandleLine> ();
			var weekends = File.Exists (weekendPath) ? ReadAllLines (weekendPath) : new List<CandleNdjsonStore.CandleLine> ();

			if (weekdays.Count == 0 && weekends.Count == 0)
				return new List<CandleNdjsonStore.CandleLine> ();

			EnsureSortedStrict (weekdays, $"{symbol} 1m weekdays");
			EnsureSortedStrict (weekends, $"{symbol} 1m weekends");

			return MergeSortedStrictUnique1m (weekdays, weekends, symbol);
			}

		private static List<CandleNdjsonStore.CandleLine> MergeSortedStrictUnique1m (
			List<CandleNdjsonStore.CandleLine> a,
			List<CandleNdjsonStore.CandleLine> b,
			string symbol )
			{
			var res = new List<CandleNdjsonStore.CandleLine> (a.Count + b.Count);

			int i = 0, j = 0;
			bool hasLast = false;
			DateTime lastTime = default;

			while (i < a.Count || j < b.Count)
				{
				CandleNdjsonStore.CandleLine next;

				if (j >= b.Count)
					{
					next = a[i++];
					}
				else if (i >= a.Count)
					{
					next = b[j++];
					}
				else
					{
					var ta = a[i].OpenTimeUtc;
					var tb = b[j].OpenTimeUtc;

					if (ta < tb)
						next = a[i++];
					else if (tb < ta)
						next = b[j++];
					else
						{
						throw new InvalidOperationException (
							$"[resample][1m] overlap between weekdays/weekends for {symbol} at OpenTimeUtc={ta:O}.");
						}
					}

				var t = next.OpenTimeUtc;
				if (hasLast && t <= lastTime)
					throw new InvalidOperationException (
						$"[resample][1m] merged non-strict time sequence for {symbol}: last={lastTime:O}, cur={t:O}.");

				res.Add (next);
				lastTime = t;
				hasLast = true;
				}

			return res;
			}

		private static void EnsureUniformStepUtc (
			List<CandleNdjsonStore.CandleLine> xs,
			TimeSpan step,
			string seriesName )
			{
			if (xs.Count == 0) return;

			EnsureSortedStrict (xs, seriesName);
			SeriesGuards.EnsureUniformStepUtc (xs, c => c.OpenTimeUtc, step, seriesName);
			}

		private static void EnsureUniformStepUtcAllowKnownGaps (
			List<CandleNdjsonStore.CandleLine> xs,
			TimeSpan step,
			string symbol,
			string interval,
			string seriesName )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));
			if (step <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException (nameof (step), step, "step must be positive.");
			if (string.IsNullOrWhiteSpace (symbol))
				throw new ArgumentException ("symbol is empty", nameof (symbol));
			if (string.IsNullOrWhiteSpace (interval))
				throw new ArgumentException ("interval is empty", nameof (interval));

			if (xs.Count == 0)
				return;

			EnsureSortedStrict (xs, seriesName);

			DateTime prev = xs[0].OpenTimeUtc;
			if (prev.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException (
					$"[series] {seriesName}: key[0] must be UTC, got Kind={prev.Kind}, t={prev:O}.");

			int knownGapCount = 0;

			for (int i = 1; i < xs.Count; i++)
				{
				DateTime cur = xs[i].OpenTimeUtc;

				if (cur.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException (
						$"[series] {seriesName}: key[{i}] must be UTC, got Kind={cur.Kind}, t={cur:O}.");

				if (cur <= prev)
					throw new InvalidOperationException (
						$"[series] {seriesName}: not strictly ascending at i={i}. prev={prev:O}, cur={cur:O}.");

				var expected = prev.Add (step);
				if (cur != expected)
					{
					if (!CandleDataGaps.TryMatchKnownGap (symbol, interval, expected, cur, out _))
						{
						throw new InvalidOperationException (
							$"[series] {seriesName}: non-uniform step at i={i}. " +
							$"prev={prev:O}, cur={cur:O}, step={cur - prev}, expected={step}.");
						}

					knownGapCount++;
					}

				prev = cur;
				}

			if (knownGapCount > 0)
				{
				Console.WriteLine (
					$"[resample] {seriesName}: допущены известные разрывы={knownGapCount}.");
				}
			}

		private static void EnsureUniformStepUtcAllowPaxgWeekendGaps (
			List<CandleNdjsonStore.CandleLine> xs,
			string seriesName )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));
			if (xs.Count == 0) return;

			EnsureSortedStrict (xs, seriesName);

			DateTime prev = xs[0].OpenTimeUtc;
			if (prev.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException (
					$"[series] {seriesName}: key[0] must be UTC, got Kind={prev.Kind}, t={prev:O}.");

			int weekendGapCount = 0;
			int gapCount = 0;
			DateTime? firstGapPrev = null;
			DateTime? firstGapCur = null;
			var step = TimeSpan.FromHours (6);
			var weekendStep = TimeSpan.FromHours (54);

			for (int i = 1; i < xs.Count; i++)
				{
				DateTime cur = xs[i].OpenTimeUtc;

				if (cur.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException (
						$"[series] {seriesName}: key[{i}] must be UTC, got Kind={cur.Kind}, t={cur:O}.");

				if (cur <= prev)
					throw new InvalidOperationException (
						$"[series] {seriesName}: not strictly ascending at i={i}. prev={prev:O}, cur={cur:O}.");

				var expected = prev.Add (step);
				if (cur != expected)
					{
					var delta = cur - prev;
					if (delta.Ticks % step.Ticks != 0)
						{
						throw new InvalidOperationException (
							$"[series] {seriesName}: non-uniform step at i={i}. " +
							$"prev={prev:O}, cur={cur:O}, step={cur - prev}, expected={step}.");
						}

					if (IsPaxgWeekendGap (prev, cur, weekendStep))
						weekendGapCount++;

					gapCount++;
					if (!firstGapPrev.HasValue)
						{
						firstGapPrev = prev;
						firstGapCur = cur;
						}
					}

				prev = cur;
				}

			if (gapCount > 0)
				{
				Console.WriteLine (
					$"[resample] {seriesName}: допущены неравномерные шаги (PAXG). " +
					$"разрывы={gapCount}, выходные={weekendGapCount}, " +
					$"пример=[{firstGapPrev:O}->{firstGapCur:O}].");
				}
			}

		private static bool IsPaxgWeekendGap ( DateTime prev, DateTime cur, TimeSpan weekendStep )
			{
			return prev.DayOfWeek == DayOfWeek.Friday
				&& prev.Hour == 18
				&& cur.DayOfWeek == DayOfWeek.Monday
				&& cur.Hour == 0
				&& cur - prev == weekendStep;
			}

		private static bool IsPaxgSymbol ( string symbol )
			{
			return string.Equals (symbol, TradingSymbols.PaxgUsdtInternal, StringComparison.OrdinalIgnoreCase);
			}

		private static void EnsureSortedStrict (
			List<CandleNdjsonStore.CandleLine> xs,
			string seriesName )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));

			SeriesGuards.SortByKeyUtcInPlace (xs, c => c.OpenTimeUtc, seriesName);
			}

		private static List<CandleNdjsonStore.CandleLine> ResampleTo6h (
			List<CandleNdjsonStore.CandleLine> src,
			bool sourceIsOneHour )
			{
			src.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
			var result = new List<CandleNdjsonStore.CandleLine> ();
			if (src.Count == 0) return result;

			DateTime Anchor6h ( DateTime tUtc )
				{
				var h = (tUtc.Hour / 6) * 6;
				return new DateTime (tUtc.Year, tUtc.Month, tUtc.Day, h, 0, 0, DateTimeKind.Utc);
				}

			DateTime curAnchor = Anchor6h (src[0].OpenTimeUtc);
			double open = src[0].Open;
			double high = src[0].High;
			double low = src[0].Low;
			double close = src[0].Close;

			void FlushIfAny ()
				{
				result.Add (new CandleNdjsonStore.CandleLine (curAnchor, open, high, low, close));
				}

			foreach (var c in src)
				{
				var a = Anchor6h (c.OpenTimeUtc);

				if (a != curAnchor)
					{
					FlushIfAny ();
					curAnchor = a;
					open = c.Open;
					high = c.High;
					low = c.Low;
					close = c.Close;
					}
				else
					{
					if (c.High > high) high = c.High;
					if (c.Low < low) low = c.Low;
					close = c.Close;
					}
				}

			FlushIfAny ();
			return result;
			}
		}
	}
