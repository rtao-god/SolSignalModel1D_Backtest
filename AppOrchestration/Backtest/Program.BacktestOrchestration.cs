using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
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
			List<PredictionRecord> records,
			List<Candle1m> sol1m
		)
			{
			// === Baseline-конфиг бэктеста (SL/TP + политики) ===
			var backtestConfig = BacktestConfigFactory.CreateBaseline ();

			// Политики, построенные из конфига (PolicyConfig → PolicySpec + ILeveragePolicy)
			var policies = BacktestPolicyFactory.BuildPolicySpecs (backtestConfig);
			Console.WriteLine ($"[policies] total = {policies.Count}");

			// Для отчёта "текущий прогноз" нужны голые ILeveragePolicy (без MarginMode и имени).
			// Хотя сейчас список не используется в этом методе, сохраняем код как есть, чтобы не ломать договорённости.
			var leveragePolicies = policies
				.Where (p => p.Policy != null)
				.Select (p => p.Policy!)
				.Cast<ILeveragePolicy> ()
				.ToList ();

			// Верхнеуровневый бэктест-раннер.
			var runner = new BacktestRunner ();

			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: sol1m,
				policies: policies,
				config: backtestConfig
			);

			// Бэктест + сохранение отчётов.
			BacktestReportsOrchestrator.SaveBacktestReports (
				mornings: mornings,
				records: records,
				sol1m: sol1m,
				policies: policies,
				backtestConfig: backtestConfig,
				nyTz: NyTz
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
