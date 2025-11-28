using System;

namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
	{
	/// <summary>
	/// Печать результатов симуляции стратегии в консоль:
	/// - капитал и risk-management;
	/// - общие метрики по дням;
	/// - PnL по сценариям;
	/// - серии по сценариям;
	/// - разрез по PredLabel.
	/// </summary>
	public static class StrategyPrinter
		{
		public static void Print ( StrategyStats stats )
			{
			if (stats == null) throw new ArgumentNullException (nameof (stats));

			Console.WriteLine ();
			Console.WriteLine ("===== Strategy Backtest (example) =====");

			// --- Капитал и объёмы ---
			Console.WriteLine ("-- Капитал и risk-management --");
			Console.WriteLine ($"Start balance              : {stats.StartBalance,12:F2} USD");
			Console.WriteLine ($"End balance                : {stats.EndBalance,12:F2} USD");
			Console.WriteLine ($"Total withdrawn profit     : {stats.TotalWithdrawnProfit,12:F2} USD");
			Console.WriteLine ($"Max drawdown               : {stats.MaxDrawdownAbs,12:F2} USD ({stats.MaxDrawdownPct * 100.0,6:F2} %)");

			if (stats.StartTotalStake > 0.0 && stats.MinTotalStake > 0.0)
				{
				double stakeDropPct =
					(1.0 - stats.MinTotalStake / stats.StartTotalStake) * 100.0;
				Console.WriteLine (
					$"Stake: start -> min        : {stats.StartTotalStake,12:F2} → {stats.MinTotalStake,12:F2} USD (-{stakeDropPct,6:F2} %)");
				}

			Console.WriteLine ();

			// --- Общие метрики по дням ---
			Console.WriteLine ("-- Общие результаты по дням --");
			Console.WriteLine ($"Trades total               : {stats.TradesCount,6}");

			Console.WriteLine ($"  profitable               : {stats.ProfitTradesCount,6}");
			Console.WriteLine ($"  lossy                    : {stats.LossTradesCount,6}");

			double winRate =
				stats.TradesCount > 0
					? (double) stats.ProfitTradesCount / stats.TradesCount * 100.0
					: 0.0;

			Console.WriteLine ($"Winrate                    : {winRate,6:F2} %");
			Console.WriteLine ($"Total PnL (net)            : {stats.TotalPnlNet,12:F2} USD");
			Console.WriteLine ($"  gross profit             : {stats.TotalProfitGross,12:F2} USD");
			Console.WriteLine ($"  gross loss               : {stats.TotalLossGross,12:F2} USD");

			Console.WriteLine ();

			// --- Сценарии 1..4 ---
			Console.WriteLine ("-- Сценарии (1–4) --");
			Console.WriteLine (
				$"Scenario 1 (base TP)       : count = {stats.Scenario1Count,5}, PnL = {stats.Scenario1Pnl,12:F2}");
			Console.WriteLine (
				$"Scenario 2 (hedge TP)      : count = {stats.Scenario2Count,5}, PnL = {stats.Scenario2Pnl,12:F2}");
			Console.WriteLine (
				$"Scenario 3 (hedge SL)      : count = {stats.Scenario3Count,5}, PnL = {stats.Scenario3Pnl,12:F2}");
			Console.WriteLine (
				$"Scenario 4 (double SL)     : count = {stats.Scenario4Count,5}, PnL = {stats.Scenario4Pnl,12:F2}");

			Console.WriteLine ();

			// --- Серии по сценариям ---
			Console.WriteLine ("-- Сценарные серии --");
			Console.WriteLine ($"Max streak S1              : {stats.MaxScenario1Streak,5}");
			Console.WriteLine ($"Max streak S2              : {stats.MaxScenario2Streak,5}");
			Console.WriteLine ($"Max streak S3              : {stats.MaxScenario3Streak,5}");
			Console.WriteLine ($"Max streak S4              : {stats.MaxScenario4Streak,5}");
			Console.WriteLine ($"Max streak hedge SL (3/4)  : {stats.MaxHedgeSlStreak,5}");

			Console.WriteLine ();

			// --- Разрез по PredLabel ---
			Console.WriteLine ("-- По типу прогноза PredLabel --");
			Console.WriteLine (
				$"Pred=2 (up)   : trades = {stats.TotalPredUpCount,5},   PnL = {stats.TotalPredUpPnl,12:F2}");
			Console.WriteLine (
				$"Pred=0 (down) : trades = {stats.TotalPredDownCount,5}, PnL = {stats.TotalPredDownPnl,12:F2}");
			Console.WriteLine (
				$"Pred=1 (flat) : trades = {stats.TotalPredFlatCount,5}, PnL = {stats.TotalPredFlatPnl,12:F2}");

			Console.WriteLine ();
			}
		}
	}
