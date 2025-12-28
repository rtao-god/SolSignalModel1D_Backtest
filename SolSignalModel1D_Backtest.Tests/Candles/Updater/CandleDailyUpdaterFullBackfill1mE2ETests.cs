using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles;

namespace SolSignalModel1D_Backtest.Tests.Candles.Updater
	{
	public sealed class CandleDailyUpdaterFullBackfill1mE2ETests
		{
		/// <summary>
		/// Полный пересбор 1m кэша (weekday + weekend) с указанной даты.
		///
		/// Инвариант безопасности:
		/// - тест должен запускаться только при явном env-флаге, чтобы исключить случайные E2E прогоны в IDE/CI.
		/// </summary>
		[EnvFact ("SOL_REBUILD_1M", "1", reason: "Enable full 1m rebuild only when SOL_REBUILD_1M=1 is explicitly set.")]
		public async Task Rebuild_SOLUSDT_1m_FullBackfill ()
			{
			var fromUtc = new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);

			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds (25) };

			var upd = new CandleDailyUpdater (
				http: http,
				symbol: "SOLUSDT",
				baseDir: PathConfig.CandlesDir,
				catchupDays: 3,
				enabledTf: CandleUpdateTf.M1);

			Console.WriteLine ($"[rebuild] start SOLUSDT 1m fullBackfillFromUtc={fromUtc:O}");

			await upd.UpdateAllAsync (fullBackfillFromUtc: fromUtc);

			var weekdayPath = CandlePaths.File ("SOLUSDT", "1m");
			var weekendPath = CandlePaths.WeekendFile ("SOLUSDT", "1m");

			var weekdayStore = new CandleNdjsonStore (weekdayPath);
			var weekendStore = new CandleNdjsonStore (weekendPath);

			var w1 = weekdayStore.TryGetFirstTimestampUtc ();
			var w2 = weekdayStore.TryGetLastTimestampUtc ();
			var e1 = weekendStore.TryGetFirstTimestampUtc ();
			var e2 = weekendStore.TryGetLastTimestampUtc ();

			Console.WriteLine ($"[rebuild] weekday first={w1:O}, last={w2:O}");
			Console.WriteLine ($"[rebuild] weekend first={e1:O}, last={e2:O}");
			}
		}
	}
