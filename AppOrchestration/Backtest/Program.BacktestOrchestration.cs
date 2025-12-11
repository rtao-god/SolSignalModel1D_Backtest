using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Количество календарных дней, за которые сохраняются отчёты истории "текущего прогноза".
		/// </summary>
		// 
		private const int CurrentPredictionHistoryWindowDays = 30;
		/// <summary>
		/// Ленивая инициализация репозитория профилей бэктеста.
		/// Внутри создаётся baseline-профиль, если файла ещё нет.
		/// </summary>
		private static async Task EnsureBacktestProfilesInitializedAsync ()
			{
			try
				{
				var profileRepo =
					new JsonBacktestProfileRepository ();

				// Достаточно один раз прочитать все профили — baseline создастся автоматически.
				await profileRepo.GetAllAsync ();
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[profiles] failed to init backtest profiles: {ex.Message}");
				}
			}

		/// <summary>
		/// Полный цикл:
		/// - сборка baseline-конфига;
		/// - построение политик и запуск BacktestRunner;
		/// - сохранение отчётов бэктеста.
		/// Стратегии по дневной модели вызываются отдельно (RunStrategyScenarios), а не отсюда.
		/// </summary>
		private static void RunBacktestAndReports (
			List<DataRow> mornings,
			List<BacktestRecord> records,
			List<Candle1m> sol1m
		)
			{
			// === Baseline-конфиг бэктеста (SL/TP + политики) ===
			var backtestConfig = BacktestConfigFactory.CreateBaseline ();

			// Политики, построенные из конфига (PolicyConfig → PolicySpec + ILeveragePolicy)
			var policies = BacktestPolicyFactory.BuildPolicySpecs (backtestConfig);
			Console.WriteLine ($"[policies] total = {policies.Count}");

			// Для отчёта "текущий прогноз" нужны голые ILeveragePolicy (без MarginMode и имени).
			var leveragePolicies = policies
				.Where (p => p.Policy != null)
				.Select (p => p.Policy!)
				.Cast<ILeveragePolicy> ()
				.ToList ();

			// === Текущий прогноз + JSON-репорт ===
			// Используются те же records и политики, что и в baseline-бэктесте.
			BacktestReportsOrchestrator.SaveCurrentPredictionReport (
				records: records,
				leveragePolicies: leveragePolicies,
				walletBalanceUsd: 200.0 // при желании можно завязать на конфиг
			);

			BacktestReportsOrchestrator.SaveCurrentPredictionHistoryReports (
				records,
				leveragePolicies,
				walletBalanceUsd: 200.0,
				historyWindowDays: CurrentPredictionHistoryWindowDays
				);

			// Верхнеуровневый бэктест-раннер.
			var runner = new BacktestRunner ();

			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: sol1m,
				policies: policies,
				config: backtestConfig,
				trainUntilUtc: _trainUntilUtc
			);

			// Бэктест + сохранение отчётов.
			BacktestReportsOrchestrator.SaveBacktestReports (
				mornings: mornings,
				records: records,
				sol1m: sol1m,
				policies: policies,
				backtestConfig: backtestConfig,
				nyTz: NyTz,
				trainUntilUtc: _trainUntilUtc
			);

			// --- Сценарные стратегии по дневной модели ---
			RunStrategyScenarios (
				mornings: mornings,
				records: records,
				sol1m: sol1m
			);
			}
		}
	}
