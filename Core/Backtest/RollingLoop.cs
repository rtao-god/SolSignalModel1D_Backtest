using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// “Тонкий” слой: порядок вызовов + печать отчётов.
	/// Датасеты ему передают извне (Program/Runner).
	/// </summary>
	public sealed class RollingLoop
		{
		public sealed class PolicySpec
			{
			public string Name { get; init; } = string.Empty;
			public ILeveragePolicy? Policy { get; init; }
			public MarginMode Margin { get; init; }
			}

		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<PolicySpec> policies,
			double dailyStopPct = 0.05,
			double dailyTpPct = 0.03 )
			{
			// 1) Модельные метрики (без микро/Delayed)
			BacktestModelStatsPrinter.Print (records);

			// 2) МИКРО: flat-only + non-flat direction
			MicroStatsPrinter.Print (mornings, records);

			// 3) PnL с/без SL
			var withSl = SimulateAllPolicies (policies, records, candles1m, useStopLoss: true, dailyStopPct, dailyTpPct);
			var noSl = SimulateAllPolicies (policies, records, candles1m, useStopLoss: false, dailyStopPct, dailyTpPct);

			// 4) Сравнение политик (две строки на каждую: with/without SL)
			PolicySlComparisonPrinter.Print (withSl, noSl);

			// 5) Детализация по политикам (WITH SL)
			PolicyBreakdownPrinter.PrintSummary (withSl, "Policy summary (WITH SL)");
			PolicyBreakdownPrinter.PrintMonthlySkew (withSl, 12);

			// 6) Delayed A/B — единый отчёт (counts + PnL%)
			DelayedStatsPrinter.Print (records);

			// 7) «Последний день каждого окна»
			WindowTailPrinter.PrintBlockTails (records, withSl, takeDays: 20, skipDays: 30);
			}

		private static List<BacktestPolicyResult> SimulateAllPolicies (
			IReadOnlyList<PolicySpec> policies,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			bool useStopLoss,
			double dailyStopPct,
			double dailyTpPct )
			{
			var results = new List<BacktestPolicyResult> (policies.Count);

			foreach (var p in policies)
				{
				if (p.Policy == null) continue;

				PnlCalculator.ComputePnL (
					records,
					candles1m,
					p.Policy,
					p.Margin,
					useStopLoss: useStopLoss,
					dailyStopPct: dailyStopPct,
					out var trades,
					out var totalPnlPct,
					out var maxDdPct,
					out var tradesBySource,
					out var withdrawnTotal,
					out var bucketSnapshots,
					out var hadLiquidation);

				results.Add (new BacktestPolicyResult
					{
					PolicyName = p.Name,
					Margin = p.Margin,
					Trades = trades,
					TotalPnlPct = totalPnlPct,
					MaxDdPct = maxDdPct,
					WithdrawnTotal = withdrawnTotal,
					HadLiquidation = hadLiquidation
					});
				}

			return results
				.OrderBy (r => r.PolicyName)
				.ThenBy (r => r.Margin.ToString ())
				.ToList ();
			}
		}
	}
