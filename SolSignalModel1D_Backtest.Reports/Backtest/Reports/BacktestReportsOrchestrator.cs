using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.PolicyRatios;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Reports.Backtest.PolicyRatios;
using SolSignalModel1D_Backtest.Reports.CurrentPrediction;
using SolSignalModel1D_Backtest.Reports.Reporting;
using SolSignalModel1D_Backtest.Reports.Reporting.Backtest;
using SolSignalModel1D_Backtest.Reports.Reporting.Ml;
using SolSignalModel1D_Backtest.Reports.Reporting.Pfi;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Reports.Backtest.Reports
	{
	public static class BacktestReportsOrchestrator
		{
		/// <summary>
		/// Окно истории для бэкфилла "текущего прогноза" (по умолчанию 60 дней).
		/// Это число можно легко править в будущем.
		/// </summary>
		public const int CurrentPredictionHistoryWindowDays = CurrentPredictionSnapshotBuilder.DefaultHistoryWindowDays;

		public static void SavePfiReports ()
			{
			try
				{
				var pfiSnapshots = FeatureImportanceSnapshots.GetSnapshots ();

				if (pfiSnapshots != null && pfiSnapshots.Count > 0)
					{
					var pfiReport = FeatureImportanceReportBuilder.BuildPerModelReport (
						pfiSnapshots,
						TableDetailLevel.Technical,
						explicitTitle: "PFI по моделям (binary)"
					);

					var storage = new ReportStorage ();
					storage.Save (pfiReport);

					Console.WriteLine ("[pfi-report] pfi_per_model report saved.");

					var modelStatsReport = ModelStatsReportBuilder.BuildFromSnapshots (
						pfiSnapshots,
						explicitTitle: "Статистика моделей (PFI / AUC)"
					);

					modelStatsReport.Kind = "ml_model_stats";

					storage.Save (modelStatsReport);

					Console.WriteLine ("[ml-model-stats] ml_model_stats report saved.");
					}
				else
					{
					Console.WriteLine ("[pfi-report] no PFI snapshots, report not built.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[pfi-report] error while building/saving PFI report: {ex.Message}");
				}
			}

		/// <summary>
		/// Сохраняет:
		/// - backtest_summary (универсальный отчёт по PnL);
		/// - backtest_baseline (упрощённый снапшот по политикам);
		/// - backtest_model_stats (не PFI: confusion + SL-модель, теперь по сегментам Train/OOS/Recent/Full);
		/// - policy_ratios (Sharpe/Sortino/Calmar по политикам baseline).
		/// </summary>
		public static void SaveBacktestReports (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			BacktestConfig backtestConfig,
			TimeZoneInfo nyTz,
			DateTime? trainUntilUtc )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));
			if (policies == null) throw new ArgumentNullException (nameof (policies));
			if (backtestConfig == null) throw new ArgumentNullException (nameof (backtestConfig));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			// --- backtest_summary ---
			try
				{
				var summary = BacktestEngine.RunBacktest (
					mornings: mornings,
					records: records,
					candles1m: sol1m,
					policies: policies,
					config: backtestConfig
				);

				BacktestSummaryPrinter.Print (summary);

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

			// --- backtest_baseline ---
			List<BacktestPolicyResult>? baselineResults = null;

			try
				{
				baselineResults = RollingLoop.SimulateAllPolicies (
					policies: policies,
					records: records,
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

					var baselineStorage = new BacktestBaselineStorage ();
					baselineStorage.Save (snapshot);

					Console.WriteLine ("[backtest-baseline] snapshot saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-baseline] error while building/saving snapshot: {ex.Message}");
				}

			// --- backtest_model_stats ---
			try
				{
				if (records.Count == 0)
					{
					Console.WriteLine ("[backtest-model-stats] no records, report not built.");
					}
				else
					{
					// 1) Упорядочиваем все PredictionRecord по дате для логов и срезов.
					var orderedRecords = records
						.OrderBy (r => r.DateUtc)
						.ToList ();

					var minDateUtc = orderedRecords.First ().DateUtc;
					var maxDateUtc = orderedRecords.Last ().DateUtc;

					Console.WriteLine (
						$"[backtest-model-stats] full period = {minDateUtc:yyyy-MM-dd}..{maxDateUtc:yyyy-MM-dd}, " +
						$"totalRecords = {orderedRecords.Count}");

					// 2) Граница train/OOS:
					// если внешняя граница не передана, считаем, что весь период — train.
					var effectiveTrainUntilUtc = trainUntilUtc ?? maxDateUtc;

					var multi = BacktestModelStatsMultiSnapshotBuilder.Build (
						allRecords: records,
						sol1m: sol1m,
						nyTz: nyTz,
						dailyTpPct: backtestConfig.DailyTpPct,
						dailySlPct: backtestConfig.DailyStopPct,
						trainUntilUtc: trainUntilUtc ?? records.Max (r => r.DateUtc),
						recentDays: 240,
						runKind: ModelRunKind.Analytics
					);

					var statsReport = BacktestModelStatsReportBuilder.Build (multi);

					var storage = new ReportStorage ();
					storage.Save (statsReport);

					Console.WriteLine ("[backtest-model-stats] backtest_model_stats report saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-model-stats] error while building/saving report: {ex.Message}");
				}

			// --- backtest_policy_ratios (baseline) ---
			try
				{
				if (baselineResults == null || baselineResults.Count == 0)
					{
					Console.WriteLine ("[backtest-policy-ratios] no baseline results, report not built.");
					}
				else
					{
					// baseline → считаем, что backtestId = "baseline"
					const string backtestId = "baseline";

					var ratiosSnapshot = PolicyRatiosSnapshotBuilder.Build (
						baselineResults,
						backtestId: backtestId
					);

					DateTime? fromDateUtc = null;
					DateTime? toDateUtc = null;

					if (records.Count > 0)
						{
						fromDateUtc = records.Min (r => r.DateUtc);
						toDateUtc = records.Max (r => r.DateUtc);
						}

					var ratiosReport = PolicyRatiosReportBuilder.Build (
						ratiosSnapshot,
						fromDateUtc,
						toDateUtc
					);

					var storage = new ReportStorage ();
					storage.SaveTyped ("policy_ratios", backtestId, ratiosReport);

					Console.WriteLine ("[backtest-policy-ratios] policy_ratios report saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-policy-ratios] error while building/saving report: {ex.Message}");
				}
			}

		/// <summary>
		/// Строит и сохраняет ОДИН отчёт "текущий прогноз" по последней записи.
		/// Поведение старого API сохранено.
		/// </summary>
		public static void SaveCurrentPredictionReport (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<ILeveragePolicy> leveragePolicies,
			double walletBalanceUsd = 200.0 )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (leveragePolicies == null) throw new ArgumentNullException (nameof (leveragePolicies));

			try
				{
				var currentSnapshot = CurrentPredictionSnapshotBuilder.Build (
					records: records,
					policies: leveragePolicies,
					walletBalanceUsd: walletBalanceUsd
				);

				// Берём глобальные PFI-снимки и добавляем топ-фичи по нужной модели.
				CurrentPredictionPfiExplanation.AppendTopFeaturesFromGlobalSnapshots (
					snapshot: currentSnapshot,
					tagFilter: "train:dir-normal"
				);

				var pfiSnapshots = FeatureImportanceSnapshots.GetSnapshots ();
				var dirSnapshot = pfiSnapshots
					.LastOrDefault (s => s.Tag.Contains ("dir", StringComparison.OrdinalIgnoreCase));

				if (dirSnapshot != null)
					{
					CurrentPredictionPfiExplanation.AppendTopFeaturesFromSnapshot (
						snapshot: currentSnapshot,
						pfiSnapshot: dirSnapshot
					);
					}

				if (currentSnapshot == null)
					{
					Console.WriteLine ("[current-report] snapshot not built (no records or policies).");
					return;
					}

				// Полный вывод в консоль (включая ExplanationItems).
				CurrentPredictionPrinter.Print (currentSnapshot);

				var report = CurrentPredictionReportBuilder.Build (currentSnapshot);

				if (report == null)
					{
					Console.WriteLine ("[current-report] report not built from snapshot.");
					return;
					}

				var storage = new ReportStorage ();
				storage.Save (report);
				Console.WriteLine ("[current-report] current_prediction report saved.");
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[current-report] error while building/saving snapshot/report: {ex.Message}");
				}
			}

		/// <summary>
		/// Бэкфилл отчётов "текущий прогноз" за последние historyWindowDays дней.
		/// Для каждого дня строится снапшот + ReportDocument (kind = "current_prediction").
		/// Отчёты сохраняются через ReportStorage.
		/// </summary>
		public static void SaveCurrentPredictionHistoryReports (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<ILeveragePolicy> leveragePolicies,
			double walletBalanceUsd = 200.0,
			int historyWindowDays = CurrentPredictionHistoryWindowDays )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (leveragePolicies == null) throw new ArgumentNullException (nameof (leveragePolicies));

			if (records.Count == 0)
				{
				Console.WriteLine ("[current-report-history] no records, history not built.");
				return;
				}

			if (leveragePolicies.Count == 0)
				{
				Console.WriteLine ("[current-report-history] no leverage policies, history not built.");
				return;
				}

			if (historyWindowDays <= 0)
				{
				historyWindowDays = CurrentPredictionHistoryWindowDays;
				}

			try
				{
				var snapshots = CurrentPredictionSnapshotBuilder.BuildHistory (
					records: records,
					policies: leveragePolicies,
					walletBalanceUsd: walletBalanceUsd,
					historyWindowDays: historyWindowDays
				);

				if (snapshots.Count == 0)
					{
					Console.WriteLine ($"[current-report-history] no snapshots for last {historyWindowDays} days.");
					return;
					}

				var storage = new ReportStorage ();

				foreach (var snapshot in snapshots)
					{
					CurrentPredictionPfiExplanation.AppendTopFeaturesFromGlobalSnapshots (
						snapshot,
						tagFilter: "train:dir-normal"
					);

					var report = CurrentPredictionReportBuilder.Build (snapshot);
					storage.Save (report);
					}

				var minDateUtc = snapshots.First ().PredictionDateUtc.Date;
				var maxDateUtc = snapshots.Last ().PredictionDateUtc.Date;

				Console.WriteLine (
					$"[current-report-history] saved {snapshots.Count} current_prediction reports " +
					$"for period {minDateUtc:yyyy-MM-dd}..{maxDateUtc:yyyy-MM-dd} (windowDays={historyWindowDays}).");
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[current-report-history] error while building/saving history: {ex.Message}");
				}
			}

		/// <summary>
		/// Строит и сохраняет отчёт "текущий прогноз" за конкретную дату (UTC).
		/// Берётся последняя PredictionRecord с DateUtc.Date == predictionDateUtc.Date.
		/// Это заготовка под API/фронт для выбора даты из всей выборки.
		/// </summary>
		public static void SaveCurrentPredictionReportForDate (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<ILeveragePolicy> leveragePolicies,
			DateTime predictionDateUtc,
			double walletBalanceUsd = 200.0 )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (leveragePolicies == null) throw new ArgumentNullException (nameof (leveragePolicies));

			if (records.Count == 0)
				{
				Console.WriteLine ("[current-report-by-date] no records, report not built.");
				return;
				}

			if (leveragePolicies.Count == 0)
				{
				Console.WriteLine ("[current-report-by-date] no leverage policies, report not built.");
				return;
				}

			try
				{
				var snapshot = CurrentPredictionSnapshotBuilder.BuildForDate (
					records: records,
					policies: leveragePolicies,
					walletBalanceUsd: walletBalanceUsd,
					predictionDateUtc: predictionDateUtc
				);

				// Выводим в консоль именно выбранный день.
				CurrentPredictionPrinter.Print (snapshot);

				var report = CurrentPredictionReportBuilder.Build (snapshot);

				var storage = new ReportStorage ();
				storage.Save (report);

				Console.WriteLine (
					$"[current-report-by-date] current_prediction report saved for {snapshot.PredictionDateUtc:yyyy-MM-dd} (UTC).");
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[current-report-by-date] error while building/saving report: {ex.Message}");
				}
			}
		}
	}
