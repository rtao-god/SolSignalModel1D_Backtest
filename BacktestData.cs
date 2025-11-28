using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Сбор всех данных, необходимых для бэктеста/превью:
		/// - обновляет свечи SOL/BTC/PAXG;
		/// - ресэмплит 6h;
		/// - строит дневные строки и утренние точки (mornings);
		/// - прогоняет дневную + микро-модель и SL-модель;
		/// - заполняет Delayed A;
		/// - возвращает snapshot для BacktestEngine / BacktestPreviewService.
		/// </summary>
		public static async Task<BacktestDataSnapshot> BuildBacktestDataAsync ()
			{
			Console.WriteLine ($"[paths] CandlesDir    = {PathConfig.CandlesDir}");
			Console.WriteLine ($"[paths] IndicatorsDir = {PathConfig.IndicatorsDir}");

			using var http = new HttpClient ();

			// --- 1. обновляем свечи (сетевой блок) ---
			Console.WriteLine ("[update] Updating SOL/BTC/PAXG candles...");

			var solSym = "SOLUSDT";
			var btcSym = "BTCUSDT";
			var paxgSym = "PAXGUSDT";

			var solUpdater = new CandleDailyUpdater (
				http,
				solSym,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var btcUpdater = new CandleDailyUpdater (
				http,
				btcSym,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var paxgUpdater = new CandleDailyUpdater (
				http,
				paxgSym,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			await Task.WhenAll
			(
				solUpdater.UpdateAllAsync (),
				btcUpdater.UpdateAllAsync (),
				paxgUpdater.UpdateAllAsync ()
			);

			Console.WriteLine ("[update] Candle update done.");

			// --- 2. ресэмплинг и загрузка всех таймфреймов ---
			List<Candle6h> solAll6h;
			List<Candle6h> btcAll6h;
			List<Candle6h> paxgAll6h;
			List<Candle1h> solAll1h;
			List<Candle1m> sol1m;

			CandleResampler.Ensure6hAvailable (solSym);
			CandleResampler.Ensure6hAvailable (btcSym);
			CandleResampler.Ensure6hAvailable (paxgSym);

			solAll6h = ReadAll6h (solSym);
			btcAll6h = ReadAll6h (btcSym);
			paxgAll6h = ReadAll6h (paxgSym);

			if (solAll6h.Count == 0 || btcAll6h.Count == 0 || paxgAll6h.Count == 0)
				throw new InvalidOperationException ("[init] Пустые 6h серии: SOL/BTC/PAXG. Проверь cache/candles/*.ndjson");

			Console.WriteLine ($"[6h] SOL={solAll6h.Count}, BTC={btcAll6h.Count}, PAXG={paxgAll6h.Count}");

			solAll1h = ReadAll1h (solSym);
			Console.WriteLine ($"[1h] SOL count = {solAll1h.Count}");

			sol1m = ReadAll1m (solSym);
			Console.WriteLine ($"[1m] SOL count = {sol1m.Count}");
			if (sol1m.Count == 0)
				throw new InvalidOperationException ("[init] Нет 1m свечей SOL/USDT в cache/candles.");

			// --- 3. диапазон дат и индикаторы ---
			var lastUtc = solAll6h.Max (c => c.OpenTimeUtc);
			var fromUtc = lastUtc.Date.AddDays (-540);
			var toUtc = lastUtc.Date;

			var indicators = new IndicatorsDailyUpdater (http);

			await indicators.UpdateAllAsync (fromUtc.AddDays (-90), toUtc, IndicatorsDailyUpdater.FillMode.NeutralFill);
			indicators.EnsureCoverageOrFail (fromUtc.AddDays (-90), toUtc);

			// --- 4. построение дневных строк ---
			DailyRowsBundle rowsBundle = await BuildDailyRowsAsync (
				indicators, fromUtc, toUtc,
				solAll6h, btcAll6h, paxgAll6h,
				sol1m
			);

			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// --- 5. предсказания и forward-метрики ---
			List<PredictionRecord> records;

				{
				var engine = CreatePredictionEngineOrFallback (allRows);

				records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);
				Console.WriteLine ($"[records] built = {records.Count}");
				}

			// --- 6. SL-модель ---
				{
				var slTrainRows = allRows
					.Where (r => r.Date <= _trainUntilUtc)
					.ToList ();

				TrainAndApplySlModelOffline (
					allRows: slTrainRows,
					records: records,
					sol1h: solAll1h,
					sol1m: sol1m,
					solAll6h: solAll6h
				);
				}

			// --- 7. Delayed A по минуткам ---
				{
				PopulateDelayedA (
					records: records,
					allRows: allRows,
					sol1h: solAll1h,
					solAll6h: solAll6h,
					sol1m: sol1m,
					dipFrac: 0.005,
					tpPct: 0.010,
					slPct: 0.010
				);
				}

			// Снэпшот для бэктеста/превью
			return new BacktestDataSnapshot
				{
				Mornings = mornings,
				Records = records,
				Candles1m = sol1m
				};
			}
		}
	}
