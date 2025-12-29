using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles
	{
	/// <summary>
	/// Компактная preflight-диагностика перед апдейтом свечей.
	/// </summary>
	public static class CandleUpdatePreflight
		{
		public sealed record TfProbe (
			string Tf,
			string Path,
			bool Exists,
			DateTime? FirstUtc,
			string? Problem );

		public sealed record Result (
			string Symbol,
			CandleUpdateTf EnabledTf,
			bool NeedsFullBackfill,
			IReadOnlyList<string> Reasons,
			IReadOnlyList<string> Warnings,
			IReadOnlyList<TfProbe> Probes )
			{
			public string ToCompactLogLine ( DateTime fullBackfillFromUtc )
				{
				var mode = NeedsFullBackfill ? "FULL" : "tail";
				var tfStr = FormatTf (EnabledTf);

				var parts = new List<string> (capacity: 6)
					{
					$"[update-check] {Symbol}: mode={mode}, tf=[{tfStr}]"
					};

				if (NeedsFullBackfill && Reasons.Count > 0)
					{
					parts.Add ("reasons=[" + string.Join ("; ", Reasons) + "]");
					parts.Add ($"fullFrom={fullBackfillFromUtc:O}");
					}

				if (Warnings.Count > 0)
					parts.Add ("WARN=[" + string.Join ("; ", Warnings) + "]");

				return string.Join (" ", parts);
				}
			}

		public static Result Evaluate (
			string symbol,
			CandleUpdateTf enabledTf,
			DateTime fullBackfillFromUtc,
			string candlesBaseDir )
			{
			if (string.IsNullOrWhiteSpace (symbol))
				throw new ArgumentException ("symbol is null/empty", nameof (symbol));
			if (string.IsNullOrWhiteSpace (candlesBaseDir))
				throw new ArgumentException ("candlesBaseDir is null/empty", nameof (candlesBaseDir));

			var reasons = new List<string> (capacity: 8);
			var warnings = new List<string> (capacity: 4);
			var probes = new List<TfProbe> (capacity: 6);

			// Важно:
			// weekend-файл по контракту НЕ содержит будние минуты.
			// Поэтому требование "first <= FullBackfillFromUtc" для weekend-файла неверно,
			// если FullBackfillFromUtc попадает на будний день — будет вечный FULL.
			var expectedFirstWeekendUtc = ExpectedFirstWeekendUtc (fullBackfillFromUtc);

			// 1m (будни) + 1m-weekends считаются обязательными, если включён M1.
			if ((enabledTf & CandleUpdateTf.M1) != 0)
				{
				ProbeTfFile (
					symbol,
					tf: "1m",
					pathFromCandlePaths: CandlePaths.File (symbol, "1m"),
					expectedPathFromBaseDir: Path.Combine (candlesBaseDir, $"{symbol}-1m.ndjson"),
					requiredFirstUtcAtOrBefore: fullBackfillFromUtc,
					requiredFirstUtcLabel: "FullBackfillFromUtc",
					reasons,
					warnings,
					probes);

				ProbeTfFile (
					symbol,
					tf: "1m-weekends",
					pathFromCandlePaths: CandlePaths.WeekendFile (symbol, "1m"),
					expectedPathFromBaseDir: Path.Combine (candlesBaseDir, $"{symbol}-1m-weekends.ndjson"),
					requiredFirstUtcAtOrBefore: expectedFirstWeekendUtc,
					requiredFirstUtcLabel: "ExpectedFirstWeekendUtc",
					reasons,
					warnings,
					probes);
				}

			if ((enabledTf & CandleUpdateTf.H1) != 0)
				{
				ProbeTfFile (
					symbol,
					tf: "1h",
					pathFromCandlePaths: CandlePaths.File (symbol, "1h"),
					expectedPathFromBaseDir: Path.Combine (candlesBaseDir, $"{symbol}-1h.ndjson"),
					requiredFirstUtcAtOrBefore: fullBackfillFromUtc,
					requiredFirstUtcLabel: "FullBackfillFromUtc",
					reasons,
					warnings,
					probes);
				}

			if ((enabledTf & CandleUpdateTf.H6) != 0)
				{
				ProbeTfFile (
					symbol,
					tf: "6h",
					pathFromCandlePaths: CandlePaths.File (symbol, "6h"),
					expectedPathFromBaseDir: Path.Combine (candlesBaseDir, $"{symbol}-6h.ndjson"),
					requiredFirstUtcAtOrBefore: fullBackfillFromUtc,
					requiredFirstUtcLabel: "FullBackfillFromUtc",
					reasons,
					warnings,
					probes);
				}

			var needsFull = reasons.Count > 0;

			return new Result (
				Symbol: symbol,
				EnabledTf: enabledTf,
				NeedsFullBackfill: needsFull,
				Reasons: reasons,
				Warnings: warnings,
				Probes: probes);
			}

		private static void ProbeTfFile (
			string symbol,
			string tf,
			string pathFromCandlePaths,
			string expectedPathFromBaseDir,
			DateTime requiredFirstUtcAtOrBefore,
			string requiredFirstUtcLabel,
			List<string> reasons,
			List<string> warnings,
			List<TfProbe> probes )
			{
			// Диагностика рассинхрона путей.
			// Это частая причина "вечного full": файлы лежат там, куда пишет апдейтер,
			// но проверки/чтение смотрят в другую директорию.
			if (!PathsEqual (pathFromCandlePaths, expectedPathFromBaseDir))
				{
				warnings.Add ($"path-mismatch {tf}: CandlePaths='{pathFromCandlePaths}' vs baseDir='{expectedPathFromBaseDir}'");
				}

			var exists = File.Exists (pathFromCandlePaths);
			DateTime? first = null;
			string? problem = null;

			if (!exists)
				{
				reasons.Add ($"missing {Path.GetFileName (pathFromCandlePaths)}");
				problem = "missing";
				probes.Add (new TfProbe (tf, pathFromCandlePaths, exists, first, problem));
				return;
				}

			// Дешёвая проверка "обрубленности" истории: читаем первую свечу.
			// Это O(1) по размеру файла (первая непустая строка).
			var store = new CandleNdjsonStore (pathFromCandlePaths);
			first = store.TryGetFirstTimestampUtc ();

			if (!first.HasValue)
				{
				reasons.Add ($"empty {Path.GetFileName (pathFromCandlePaths)}");
				problem = "empty";
				probes.Add (new TfProbe (tf, pathFromCandlePaths, exists, first, problem));
				return;
				}

			// Ключевая проверка "полноты":
			// - для обычных TF-файлов: first <= FullBackfillFromUtc;
			// - для weekend-файла: first <= ExpectedFirstWeekendUtc(FullBackfillFromUtc),
			//   иначе будет вечный FULL, если fullBackfillFromUtc попадает на будний день.
			if (first.Value > requiredFirstUtcAtOrBefore)
				{
				reasons.Add (
					$"incomplete {Path.GetFileName (pathFromCandlePaths)} first={first.Value:O} (> {requiredFirstUtcLabel}={requiredFirstUtcAtOrBefore:O})");
				problem = $"incomplete(first>{requiredFirstUtcLabel})";
				probes.Add (new TfProbe (tf, pathFromCandlePaths, exists, first, problem));
				return;
				}

			probes.Add (new TfProbe (tf, pathFromCandlePaths, exists, first, problem));
			}

		private static DateTime ExpectedFirstWeekendUtc ( DateTime fromUtc )
			{
			var t = fromUtc.ToUniversalTime ();

			// Нормализация под минутную сетку NDJSON.
			t = new DateTime (t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);

			// Выходные гарантированно встретятся в пределах 8 дней.
			for (int i = 0; i < 60 * 24 * 8; i++)
				{
				if (t.IsWeekendUtc ())
					return t;

				t = t.AddMinutes (1);
				}

			throw new InvalidOperationException (
				$"[update-check] Failed to locate weekend within 8 days from {fromUtc:O}. Check IsWeekendUtc() logic/timezone.");
			}

		private static bool PathsEqual ( string a, string b )
			{
			try
				{
				var fa = Path.GetFullPath (a).TrimEnd (Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				var fb = Path.GetFullPath (b).TrimEnd (Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				return string.Equals (fa, fb, StringComparison.OrdinalIgnoreCase);
				}
			catch
				{
				return false;
				}
			}

		private static string FormatTf ( CandleUpdateTf tfs )
			{
			var xs = new List<string> (capacity: 3);
			if ((tfs & CandleUpdateTf.M1) != 0) xs.Add ("1m");
			if ((tfs & CandleUpdateTf.H1) != 0) xs.Add ("1h");
			if ((tfs & CandleUpdateTf.H6) != 0) xs.Add ("6h");
			return xs.Count == 0 ? "-" : string.Join (",", xs);
			}
		}
	}
