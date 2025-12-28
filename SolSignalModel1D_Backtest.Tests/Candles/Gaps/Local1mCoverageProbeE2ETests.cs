using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Tests.Candles.Gaps
	{
	public sealed class Local1mCoverageProbeE2ETests
		{
		[Fact]
		public async Task Probe_SOLUSDT_1m_LocalFiles_Against_Binance_ForSuspiciousWindow ()
			{
			const string symbol = "SOLUSDT";
			const string interval = "1m";

			// Окно вокруг твоей "unknown" дыры:
			// expected=2025-12-14 08:26Z, actual=2025-12-16 00:00Z.
			var fromUtc = new DateTime (2025, 12, 14, 8, 0, 0, DateTimeKind.Utc);
			var toUtc = new DateTime (2025, 12, 16, 0, 10, 0, DateTimeKind.Utc);

			Console.WriteLine ($"[probe] range=[{fromUtc:O}..{toUtc:O}]");

			// 1) ЛОКАЛЬНЫЕ файлы (weekdays/weekends отдельно).
			var weekdayPath = CandlePaths.File (symbol, interval);
			var weekendPath = CandlePaths.WeekendFile (symbol, interval);

			Console.WriteLine ($"[probe] weekdayPath='{weekdayPath}'");
			Console.WriteLine ($"[probe] weekendPath='{weekendPath}'");

			var weekdayLines = new CandleNdjsonStore (weekdayPath).ReadRange (fromUtc, toUtc);
			var weekendLines = new CandleNdjsonStore (weekendPath).ReadRange (fromUtc, toUtc);

			Console.WriteLine ($"[probe] local weekday bars={weekdayLines.Count}");
			Console.WriteLine ($"[probe] local weekend bars={weekendLines.Count}");

			var localTimes = new List<DateTime> (weekdayLines.Count + weekendLines.Count);
			localTimes.AddRange (weekdayLines.Select (x => x.OpenTimeUtc));
			localTimes.AddRange (weekendLines.Select (x => x.OpenTimeUtc));

			localTimes.Sort ();

			ValidateStrictUnique (localTimes, tag: "local-merged");
			var localGap = FindFirstGap (localTimes, TimeSpan.FromMinutes (1));

			if (localTimes.Count == 0)
				{
				Console.WriteLine ("[probe][local] EMPTY in window (both files).");
				}
			else
				{
				Console.WriteLine ($"[probe][local] first={localTimes[0]:O}, last={localTimes[localTimes.Count - 1]:O}");
				}

			if (localGap != null)
				{
				Console.WriteLine (
					$"[probe][local] GAP: expected={localGap.Value.Expected:O} actual={localGap.Value.Actual:O} " +
					$"missingMinutes={(localGap.Value.Actual - localGap.Value.Expected).TotalMinutes}");
				}
			else
				{
				Console.WriteLine ("[probe][local] no gaps detected in merged local window.");
				}

			// 2) BINANCE API для того же окна (через твой DataLoading).
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds (25) };

			var raw = await DataLoading.GetBinanceKlinesRange (http, symbol, interval, fromUtc, toUtc);
			Console.WriteLine ($"[probe][binance] bars={raw.Count}");

			var binTimes = raw.Select (x => x.openUtc).ToList ();
			binTimes.Sort ();

			ValidateStrictUnique (binTimes, tag: "binance-window");
			var binGap = FindFirstGap (binTimes, TimeSpan.FromMinutes (1));

			if (binTimes.Count == 0)
				{
				throw new InvalidOperationException ("[probe][binance] EMPTY response in window -> сеть/лимиты/параметры запроса.");
				}

			Console.WriteLine ($"[probe][binance] first={binTimes[0]:O}, last={binTimes[binTimes.Count - 1]:O}");

			if (binGap != null)
				{
				throw new InvalidOperationException (
					$"[probe][binance] UNEXPECTED GAP in API window: expected={binGap.Value.Expected:O} actual={binGap.Value.Actual:O}. " +
					"Если это реально, оно обязано было всплыть в BinanceGapDiscovery.");
				}

			Console.WriteLine ("[probe][binance] no gaps in API window.");

			// 3) Интерпретация результата прямо в тесте (без догадок):
			// - если localGap != null при binGap == null -> проблема в локальном кеше/чтении/сборке.
			// - если localGap == null -> проблема НЕ в сырых файлах (тогда ищем место, где пишется candle-gap-hit).
			if (localGap != null)
				{
				throw new InvalidOperationException (
					"[probe] Local cache has a gap while Binance API doesn't. " +
					"НЕ добавлять это в KnownGaps — нужно чинить/пересобирать локальные 1m файлы.");
				}
			}

		private static void ValidateStrictUnique ( List<DateTime> times, string tag )
			{
			for (int i = 1; i < times.Count; i++)
				{
				if (times[i] <= times[i - 1])
					{
					throw new InvalidOperationException (
						$"[probe][{tag}] non-strict/duplicate at idx={i}: prev={times[i - 1]:O}, cur={times[i]:O}");
					}
				}
			}

		private static (DateTime Expected, DateTime Actual)? FindFirstGap ( List<DateTime> times, TimeSpan step )
			{
			for (int i = 1; i < times.Count; i++)
				{
				var expected = times[i - 1] + step;
				var actual = times[i];
				if (actual != expected)
					return (expected, actual);
				}
			return null;
			}
		}
	}
