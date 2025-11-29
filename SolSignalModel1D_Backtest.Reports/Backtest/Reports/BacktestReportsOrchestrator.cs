using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Snapshots;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using SolSignalModel1D_Backtest.Reports.CurrentPrediction;
using SolSignalModel1D_Backtest.Reports.Model;
using SolSignalModel1D_Backtest.Reports.Reporting;
using SolSignalModel1D_Backtest.Reports.Reporting.Backtest;
using SolSignalModel1D_Backtest.Reports.Reporting.Ml;
using SolSignalModel1D_Backtest.Reports.Reporting.Pfi;
using SolSignalModel1D_Backtest.Reports; // для ReportStorage и BacktestBaselineStorage

namespace SolSignalModel1D_Backtest.Reports.Backtest.Reports
	{
	/// <summary>
	/// Центральное место, где собираются и сохраняются все отчёты бэктеста:
	/// - PFI по моделям + PFI-статистика;
	/// - backtest_summary / backtest_baseline;
	/// - backtest_model_stats (не PFI, confusion/SL);
	/// - current_prediction.
	/// </summary>
	public static class BacktestReportsOrchestrator
		{
		/// <summary>
		/// Сохраняет:
		/// - PFI per model;
		/// - PFI-based статистику моделей (ml_model_stats).
		/// Математика полностью совпадает с тем, что было в Program.Main.
		/// </summary>
		public static void SavePfiReports ()
			{
			try
				{
				var pfiSnapshots = FeatureImportanceSnapshots.GetSnapshots ();

				if (pfiSnapshots != null && pfiSnapshots.Count > 0)
					{
					// PFI по моделям
					var pfiReport = FeatureImportanceReportBuilder.BuildPerModelReport (
						pfiSnapshots,
						TableDetailLevel.Technical,
						explicitTitle: "PFI по моделям (binary)"
					);

					var storage = new ReportStorage ();
					storage.Save (pfiReport);

					Console.WriteLine ("[pfi-report] pfi_per_model report saved.");

					// PFI-статистика моделей (старый репорт, Kind = ml_model_stats)
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
		/// - backtest_model_stats (не PFI: confusion + SL-модель).
		/// </summary>
		public static void SaveBacktestReports (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			BacktestConfig backtestConfig,
			TimeZoneInfo nyTz )
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

				// Консольный принтер сводки — как и раньше.
				Core.Analytics.Backtest.BacktestSummaryPrinter.Print (summary);

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
			try
				{
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

					var baselineStorage = new BacktestBaselineStorage ();
					baselineStorage.Save (snapshot);

					Console.WriteLine ("[backtest-baseline] snapshot saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-baseline] error while building/saving snapshot: {ex.Message}");
				}

			// --- backtest_model_stats (не PFI, confusion + SL-модель) ---
			try
				{
				if (records.Count == 0)
					{
					Console.WriteLine ("[backtest-model-stats] no records, report not built.");
					}
				else
					{
					// Используем готовый билдёр, полностью копирующий консольную логику.
					var statsSnapshot = BacktestModelStatsSnapshotBuilder.Compute (
						records: records,
						sol1m: sol1m,
						dailyTpPct: backtestConfig.DailyTpPct,
						dailySlPct: backtestConfig.DailyStopPct,
						nyTz: nyTz
					);

					var statsReport = BacktestModelStatsReportBuilder.Build (statsSnapshot);

					var storage = new ReportStorage ();
					storage.Save (statsReport);

					Console.WriteLine ("[backtest-model-stats] backtest_model_stats report saved.");
					}
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[backtest-model-stats] error while building/saving report: {ex.Message}");
				}
			}

		/// <summary>
		/// Сохраняет snapshot + отчёт по текущему прогнозу (current_prediction).
		/// </summary>
		public static void SaveCurrentPredictionReport (
			IReadOnlyList<PredictionRecord> records,
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

				if (currentSnapshot == null)
					{
					Console.WriteLine ("[current-report] snapshot not built (no records or policies).");
					return;
					}

				// Явно указываем нужный принтер, чтобы не было неоднозначности.
				SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction.CurrentPredictionPrinter
					.Print (currentSnapshot);

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
		}
	}
