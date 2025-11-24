using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Diagnostics.Daily;
using SolSignalModel1D_Backtest.Core.Analytics.Reports;
using SolSignalModel1D_Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Model;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		private sealed class DailyRowsBundle
			{
			public List<DataRow> AllRows { get; init; } = new ();
			public List<DataRow> Mornings { get; init; } = new ();
			}

		public static async Task Main ( string[] args )
			{
			Console.WriteLine ($"[paths] CandlesDir    = {PathConfig.CandlesDir}");
			Console.WriteLine ($"[paths] IndicatorsDir = {PathConfig.IndicatorsDir}");

			using var http = new HttpClient ();

			// --- 1. обновляем свечи (сетевой блок) ---
			Console.WriteLine ("[update] Updating SOL/BTC/PAXG candles...");

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

			await Task.WhenAll
			(
				solUpdater.UpdateAllAsync (),
				btcUpdater.UpdateAllAsync (),
				paxgUpdater.UpdateAllAsync ()
			);

			Console.WriteLine ("[update] Candle update done.");

			var solSym = "SOLUSDT";
			var btcSym = "BTCUSDT";
			var paxgSym = "PAXGUSDT";

			List<Candle6h> solAll6h = null!;
			List<Candle6h> btcAll6h = null!;
			List<Candle6h> paxgAll6h = null!;
			List<Candle1h> solAll1h = null!;
			List<Candle1m> sol1m = null!;

			// --- 2. ресэмплинг и загрузка всех таймфреймов ---
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
				throw new InvalidOperationException ("[init] Нет 1m свечей SOLUSDT в cache/candles.");

			var lastUtc = solAll6h.Max (c => c.OpenTimeUtc);
			var fromUtc = lastUtc.Date.AddDays (-540);
			var toUtc = lastUtc.Date;

			var indicators = new IndicatorsDailyUpdater (http);

			// --- 3. индикаторы и проверка покрытия ---
			await indicators.UpdateAllAsync (fromUtc.AddDays (-90), toUtc, IndicatorsDailyUpdater.FillMode.NeutralFill);
			indicators.EnsureCoverageOrFail (fromUtc.AddDays (-90), toUtc);

			// === ДНЕВНЫЕ СТРОКИ ===
			DailyRowsBundle rowsBundle = null!;

			// --- 4. построение дневных строк ---
			rowsBundle = await BuildDailyRowsAsync (
				indicators, fromUtc, toUtc,
				solAll6h, btcAll6h, paxgAll6h,
				sol1m
			);

			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// === МОДЕЛЬ (микро-слой + дневная схема через PredictionEngine) ===
			List<PredictionRecord> records = null!;

			// --- 5. предсказания и forward-метрики ---
				{
				var engine = CreatePredictionEngineOrFallback (allRows);

				records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);
				Console.WriteLine ($"[records] built = {records.Count}");
				}

			// --- 5a. PFI для дневных моделей ---
				{
				var dailyTrainRows = allRows
					.Where (r => r.Date <= _trainUntilUtc)
					.ToList ();

				var dailyOosRows = allRows
					.Where (r => r.Date > _trainUntilUtc)
					.ToList ();

				if (dailyTrainRows.Count < 50)
					{
					Console.WriteLine ($"[pfi:daily] not enough train rows for PFI (count={dailyTrainRows.Count}), skip.");
					}
				else
					{
					var dailyTrainer = new ModelTrainer ();

					var bundle = dailyTrainer.TrainAll (dailyTrainRows, datesToExclude: null);

					DailyModelDiagnostics.LogFeatureImportanceOnDailyModels (bundle, dailyTrainRows, "train");

					if (dailyOosRows.Count > 0)
						{
						var tag = dailyOosRows.Count >= 50 ? "oos" : "oos-small";
						DailyModelDiagnostics.LogFeatureImportanceOnDailyModels (bundle, dailyOosRows, tag);
						}
					else
						{
						Console.WriteLine ("[pfi:daily] no OOS rows after _trainUntilUtc, skip oos PFI.");
						}
					}
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

			// --- 6b. Глобальная сводка PFI ---
			FeatureImportanceAnalyzer.PrintGlobalSummary (
				topPerModel: 5,
				topGlobalFeatures: 15,
				importanceThreshold: 0.003
			);

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

			// === Baseline-конфиг бэктеста (SL/TP + политики) ===
			var backtestConfig = BacktestConfigFactory.CreateBaseline ();

			// Политики, построенные из конфига (PolicyConfig → PolicySpec + ILeveragePolicy)
			var policies = BacktestPolicyFactory.BuildPolicySpecs (backtestConfig);
			Console.WriteLine ($"[policies] total = {policies.Count}");

			// Для отчёта "текущий прогноз" нужны голые ILeveragePolicy (без MarginMode и имени)
			var leveragePolicies = policies
				.Where (p => p.Policy != null)
				.Select (p => p.Policy!)
				.Cast<ILeveragePolicy> ()
				.ToList ();

			var runner = new BacktestRunner ();

			// --- 8. верхнеуровневый бэктест/принтер ---
			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: sol1m,
				policies: policies,
				config: backtestConfig
			);

			// --- 9. Сохраняем отчёт бэктеста (backtest_summary) ---
			try
				{
				// Универсальный движок: строим BacktestSummary на основе тех же данных,
				// которые использует BacktestRunner/ RollingLoop.
				var summary = BacktestEngine.RunBacktest (
					mornings: mornings,
					records: records,
					candles1m: sol1m,
					policies: policies,
					config: backtestConfig
				);

				var backtestReport = BacktestSummaryReportBuilder.Build (summary);

				if (backtestReport == null)
					{
					Console.WriteLine ("[backtest-report] report not built (no data).");
					}
				else
					{
					var storage = new ReportStorage ();
					storage.Save (backtestReport);
					Console.WriteLine ("[backtest-report] backtest_summary report saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-report] error while building/saving report: {ex.Message}");
				}

			// --- 9b. Сохраняем baseline-снапшот бэктеста (backtest_baseline) ---
			try
				{
				// Здесь считаем baseline PnL по тем же данным, что и выше,
				// но без консольного вывода и только WITH SL, без overlay.
				var baselineResults = RollingLoop.SimulateAllPolicies (
					policies: policies,
					records: records,
					candles1m: sol1m,
					useStopLoss: true,
					config: backtestConfig,
					useAnti: false
				);

				if (baselineResults.Count == 0)
					{
					Console.WriteLine ("[backtest-baseline] no baseline results (no policies or records).");
					}
				else
					{
					var snapshot = BacktestBaselineSnapshotBuilder.Build (
						withSlBase: baselineResults,
						dailyStopPct: backtestConfig.DailyStopPct,
						dailyTpPct: backtestConfig.DailyTpPct,
						configName: "default"
					);

					var storage = new ReportStorage ();
					storage.Save (snapshot, "backtest_baseline");
					Console.WriteLine ("[backtest-baseline] snapshot saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-baseline] error while building/saving snapshot: {ex.Message}");
				}

			// --- 10. Сохраняем отчёт "текущий прогноз" ---
			try
				{
				var report = CurrentPredictionReportBuilder.Build (
					records: records,
					policies: leveragePolicies,
					walletBalanceUsd: 200.0);

				if (report == null)
					{
					Console.WriteLine ("[current-report] report not built (no records or policies).");
					}
				else
					{
					var storage = new ReportStorage ();
					storage.Save (report);
					Console.WriteLine ("[current-report] current_prediction report saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[current-report] error while building/saving report: {ex.Message}");
				}
			}
		}
	}
