using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Builders;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
	{
	internal partial class Program
		{
		/// <summary>
		/// Общая таймзона Нью-Йорка для всех расчётов.
		/// </summary>
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Результат построения дневных строк.
		/// </summary>
		private sealed class DailyRowsBundle
			{
			public List<DataRow> AllRows { get; init; } = new List<DataRow> ();
			public List<DataRow> Mornings { get; init; } = new List<DataRow> ();
			}

		public static async Task Main ( string[] args )
			{
			Console.WriteLine ($"[paths] CandlesDir    = {PathConfig.CandlesDir}");
			Console.WriteLine ($"[paths] IndicatorsDir = {PathConfig.IndicatorsDir}");

			using var http = new HttpClient ();

			// Кол-во основных блоков пайплайна (для процентов).
			const int totalSteps = 8;
			int step = 0;

			// --- 1. обновляем свечи (сетевой блок) ---
			await ConsoleBlockTimer.RunAsync (
				"SOL/BTC/PAXG",
				async () =>
				{
					var solUpdater = new CandleDailyUpdater (
						http,
						"SOLUSDT",
						PathConfig.CandlesDir,
						catchupDays: 3
					);

					var btcUpdater = new CandleDailyUpdater (
						http,
						"BTCUSDT",
						PathConfig.CandlesDir,
						catchupDays: 3
					);

					var paxgUpdater = new CandleDailyUpdater (
						http,
						"PAXGUSDT",
						PathConfig.CandlesDir,
						catchupDays: 3
					);

					Console.WriteLine ("[update] Updating SOL/BTC/PAXG candles...");
					await Task.WhenAll
						(
							solUpdater.UpdateAllAsync (),
							btcUpdater.UpdateAllAsync (),
							paxgUpdater.UpdateAllAsync ()
						);
					Console.WriteLine ("[update] Candle update done.");
				});

			// Символы без слэшей, чтобы совпадали с именами файлов в cache/candles/
			var solSym = "SOLUSDT";
			var btcSym = "BTCUSDT";
			var paxgSym = "PAXGUSDT";

			List<Candle6h> solAll6h = null!;
			List<Candle6h> btcAll6h = null!;
			List<Candle6h> paxgAll6h = null!;
			List<Candle1h> solAll1h = null!;
			List<Candle1m> sol1m = null!;

			// --- 2. ресэмплинг и загрузка всех таймфреймов ---
			await ConsoleBlockTimer.RunAsync (
				"",
				() =>
				{
					// Обеспечиваем наличие 6h (ресэмплинг из 1h/1m при надобности)
					CandleResampler.Ensure6hAvailable (solSym);
					CandleResampler.Ensure6hAvailable (btcSym);
					CandleResampler.Ensure6hAvailable (paxgSym);

					// Читаем 6h
					solAll6h = ReadAll6h (solSym);
					btcAll6h = ReadAll6h (btcSym);
					paxgAll6h = ReadAll6h (paxgSym);

					if (solAll6h.Count == 0 || btcAll6h.Count == 0 || paxgAll6h.Count == 0)
						throw new InvalidOperationException ("[init] Пустые 6h серии: SOL/BTC/PAXG. Проверь cache/candles/*.ndjson");

					Console.WriteLine ($"[6h] SOL={solAll6h.Count}, BTC={btcAll6h.Count}, PAXG={paxgAll6h.Count}");

					// 1h SOL — нужен для SL-фич
					solAll1h = ReadAll1h (solSym);
					Console.WriteLine ($"[1h] SOL count = {solAll1h.Count}");

					// Минутки SOL: нужны и для Path-based меток, и для Delayed A, и для SL-датасета
					sol1m = ReadAll1m (solSym);
					Console.WriteLine ($"[1m] SOL count = {sol1m.Count}");
					if (sol1m.Count == 0)
						throw new InvalidOperationException ("[init] Нет 1m свечей SOLUSDT в cache/candles.");
				});

			// Диапазон
			var lastUtc = solAll6h.Max (c => c.OpenTimeUtc);
			var fromUtc = lastUtc.Date.AddDays (-540);
			var toUtc = lastUtc.Date;

			var indicators = new IndicatorsDailyUpdater (http);

			// --- 3. индикаторы и проверка покрытия ---
			await ConsoleBlockTimer.RunAsync (
				": ",
				async () =>
				{
					await indicators.UpdateAllAsync (fromUtc.AddDays (-90), toUtc, IndicatorsDailyUpdater.FillMode.NeutralFill);
					indicators.EnsureCoverageOrFail (fromUtc.AddDays (-90), toUtc);
				});

			// === ДНЕВНЫЕ СТРОКИ ===
			DailyRowsBundle rowsBundle = null!;

			// --- 4. построение дневных строк ---
			await ConsoleBlockTimer.RunAsync (
				"",
				async () =>
				{
					rowsBundle = await BuildDailyRowsAsync (
						indicators, fromUtc, toUtc,
						solAll6h, btcAll6h, paxgAll6h,
						sol1m
					);
				});

			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// === МОДЕЛЬ (fallback + эвристика) ===
			List<PredictionRecord> records = null!;

			// --- 5. предсказания и forward-метрики ---
			await ConsoleBlockTimer.RunAsync (
				"",
				async () =>
				{
					// Модель (пока fallback: PredictionEngine даёт reason=fallback, а дальше — эвристика)
					var engine = CreatePredictionEngineOrFallback ();

					// PredictionRecord[] + forward (из 6h) + эвристика при fallback
					records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);
					Console.WriteLine ($"[records] built = {records.Count}");
				});

			// Например, хотим считать стратегию при марже 200 USDT.
			double walletBalanceUsd = 200.0;
			CurrentPredictionPrinter.Print (records, solAll6h, walletBalanceUsd);

			// --- 6. SL-модель: оффлайн-тренировка + SlProb/SlHighDecision ---
			await ConsoleBlockTimer.RunAsync (
				"",
				() =>
				{
					TrainAndApplySlModelOffline (
						allRows: allRows,
						records: records,
						sol1h: solAll1h,
						sol1m: sol1m,
						solAll6h: solAll6h
					);
				});

			// --- 7. Delayed A по минуткам ---
			await ConsoleBlockTimer.RunAsync (
				"",
				() =>
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
				});

			// Политики (const 2/3/5/10/15/50 × Cross/Isolated + риск-политики)
			var policies = BuildPolicies ();
			Console.WriteLine ($"[policies] total = {policies.Count}");

			var runner = new BacktestRunner ();

			// --- 8. верхнеуровневый бэктест/принтер ---
			await ConsoleBlockTimer.RunAsync (
				"",
				() =>
				{
					runner.Run (
						mornings: mornings,
						records: records,
						candles1m: sol1m,
						policies: policies,
						cfg: new BacktestRunner.Config { DailyStopPct = 0.05, DailyTpPct = 0.03 }
					);
				});
			}
		}
	}
