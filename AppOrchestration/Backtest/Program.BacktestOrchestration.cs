using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Количество календарных дней, за которые сохраняются отчёты истории "текущего прогноза".
		/// </summary>
		private const int CurrentPredictionHistoryWindowDays = 30;

		/// <summary>
		/// Ленивая инициализация репозитория профилей бэктеста.
		/// Внутри создаётся baseline-профиль, если файла ещё нет.
		/// </summary>
		private static async Task EnsureBacktestProfilesInitializedAsync ()
			{
			try
				{
				var profileRepo = new JsonBacktestProfileRepository ();

				// Достаточно один раз прочитать все профили — baseline создастся автоматически.
				await profileRepo.GetAllAsync ();
				}
			catch (Exception ex)
				{
				// Важно не падать: отсутствие профиля не должно ломать весь пайплайн.
				Console.WriteLine ($"[profiles] failed to init backtest profiles: {ex.Message}");
				}
			}

		/// <summary>
		/// Полный цикл:
		/// - сборка baseline-конфига;
		/// - построение политик и запуск BacktestRunner;
		/// - сохранение отчётов бэктеста;
		/// - сценарные стратегии по дневной модели.
		/// </summary>
		private static void RunBacktestAndReports (
			List<DataRow> mornings,
			List<BacktestRecord> records,
			List<Candle1m> sol1m )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));

			// === Baseline-конфиг бэктеста (SL/TP + политики) ===
			var backtestConfig = BacktestConfigFactory.CreateBaseline ();

			// Политики, построенные из конфига (PolicyConfig → PolicySpec + ILeveragePolicy)
			var policies = BacktestPolicyFactory.BuildPolicySpecs (backtestConfig);
			Console.WriteLine ($"[policies] total = {policies.Count}");

			// Для отчёта "текущий прогноз" нужны только ILeveragePolicy (без MarginMode и имени).
			var leveragePolicies = ExtractLeveragePolicies (policies);

			// === Текущий прогноз + JSON-репорты ===
			// Берём те же records и политики, что и в baseline-бэктесте: это важно для консистентности.
			const double WalletBalanceUsd = 200.0;

			BacktestReportsOrchestrator.SaveCurrentPredictionReport (
				records: records,
				leveragePolicies: leveragePolicies,
				walletBalanceUsd: WalletBalanceUsd
			);

			BacktestReportsOrchestrator.SaveCurrentPredictionHistoryReports (
				records,
				leveragePolicies,
				walletBalanceUsd: WalletBalanceUsd,
				historyWindowDays: CurrentPredictionHistoryWindowDays
			);

			// === Baseline backtest ===
			var runner = new BacktestRunner ();

			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: sol1m,
				policies: policies,
				config: backtestConfig,
				trainUntilUtc: _trainUntilUtc
			);

			// === Сохранение отчётов бэктеста ===
			BacktestReportsOrchestrator.SaveBacktestReports (
				mornings: mornings,
				records: records,
				sol1m: sol1m,
				policies: policies,
				backtestConfig: backtestConfig,
				nyTz: NyTz,
				trainUntilUtc: _trainUntilUtc
			);

			// === Сценарные стратегии по дневной модели ===
			RunStrategyScenarios (
				mornings: mornings,
				records: records,
				sol1m: sol1m
			);
			}

		private static List<ILeveragePolicy> ExtractLeveragePolicies (
			IReadOnlyList<RollingLoop.PolicySpec> policies )
			{
			// Важно фильтровать null явно: PolicySpec допускает отсутствие ILeveragePolicy.
			// Отчёты "текущий прогноз" не должны падать из-за таких спецификаций.
			var list = new List<ILeveragePolicy> (policies.Count);

			foreach (var p in policies)
				{
				if (p.Policy != null)
					list.Add (p.Policy);
				}

			return list;
			}
		}
	}
