using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest.Services;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private const int CurrentPredictionHistoryWindowDays = 30;

		private static async Task EnsureBacktestProfilesInitializedAsync ()
			{
			try
				{
				var profileRepo = new JsonBacktestProfileRepository ();
				await profileRepo.GetAllAsync ();
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[profiles] failed to init backtest profiles: {ex.Message}");
				}
			}

		private static void RunBacktestAndReports (
			List<LabeledCausalRow> mornings,
			List<BacktestRecord> records,
			IReadOnlyList<Candle1m> dayMinutes )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (dayMinutes == null) throw new ArgumentNullException (nameof (dayMinutes));

			var backtestConfig = BacktestConfigFactory.CreateBaseline ();

			// PolicySpec.Policy = IOmniscientLeveragePolicy (для PnL/RollingLoop).
			var policies = BacktestPolicyFactory.BuildPolicySpecs (backtestConfig);
			Console.WriteLine ($"[policies] total = {policies.Count}");

			// Для "current prediction" берём ТОЛЬКО каузальный интерфейс, без доступа к forward-фактам.
			var leveragePolicies = ExtractCausalLeveragePolicies (policies);

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

			var runner = new BacktestRunner ();

            runner.Run(
				mornings: mornings,
				records: records,
				candles1m: dayMinutes,
				policies: policies,
				config: backtestConfig,
				trainUntilExitDayKeyUtc: _trainUntilExitDayKeyUtc
			);

            BacktestReportsOrchestrator.SaveBacktestReports (
				mornings: mornings,
				records: records,
				sol1m: dayMinutes,
				policies: policies,
				backtestConfig: backtestConfig,
				nyTz: NyTz,
				trainUntilExitDayKeyUtc: _trainUntilExitDayKeyUtc
			);

			RunStrategyScenarios (
				mornings: mornings,
				records: records,
				sol1m: dayMinutes.ToList ()
			);
			}

		private static List<ICausalLeveragePolicy> ExtractCausalLeveragePolicies (
			IReadOnlyList<RollingLoop.PolicySpec> policies )
			{
			if (policies == null) throw new ArgumentNullException (nameof (policies));

			var list = new List<ICausalLeveragePolicy> (policies.Count);

			foreach (var p in policies)
				{
				if (p.Policy == null) continue;

				// Инвариант: любая политика, используемая в омнисциентном PnL,
				// обязана иметь каузальный интерфейс для построения "current prediction" без утечек.
				if (p.Policy is ICausalLeveragePolicy causal)
					{
					list.Add (causal);
					continue;
					}

				throw new InvalidOperationException (
					$"[policies] policy '{p.Name}' ({p.Policy.GetType ().FullName}) does not implement ICausalLeveragePolicy. " +
					"CurrentPrediction должен работать строго через causal-интерфейс.");
				}

			if (list.Count == 0)
				throw new InvalidOperationException ("[policies] no causal leverage policies after extraction.");

			return list;
			}
		}
	}
