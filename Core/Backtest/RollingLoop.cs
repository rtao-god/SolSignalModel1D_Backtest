using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public sealed class RollingLoop
		{
		public sealed class PolicySpec
			{
			public string Name { get; init; } = string.Empty;
			public ILeveragePolicy? Policy { get; init; }
			public MarginMode Margin { get; init; }
			}

		// =====================================================================
		// MAIN ENTRY
		// =====================================================================
		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<PolicySpec> policies,
			double dailyStopPct = 0.05,
			double dailyTpPct = 0.03 )
			{
			// 1) МИКРО-статистика
			MicroStatsPrinter.Print (mornings, records);

			// 2) Обычные прогонки WITH SL / NO SL
			var withSlBase = SimulateAllPolicies (
				policies, records, candles1m,
				useStopLoss: true,
				dailyStopPct,
				dailyTpPct,
				useAnti: false
			);

			var noSlBase = SimulateAllPolicies (
				policies, records, candles1m,
				useStopLoss: false,
				dailyStopPct,
				dailyTpPct,
				useAnti: false
			);

			// 3) Сравнение политик по SL
			PolicySlComparisonPrinter.Print (withSlBase, noSlBase);

			// 3.1) Расширенный SL отчёт
			SlPnlReportPrinter.PrintMatchedDeltaAndPnl (records, withSlBase, noSlBase);

			// 4) Policy summary (WITH SL)
			//PolicyBreakdownPrinter.PrintSummary (withSlBase, "Policy summary (WITH SL)");
			//PolicyBreakdownPrinter.PrintMonthlySkew (withSlBase, 12);

			//PolicyRatiosPrinter.Print (withSlBase, "Policy ratios (WITH SL)");
			//PolicyRatiosPrinter.Print (noSlBase, "Policy ratios (NO SL)");

			// 5) Delayed A/B
			//DelayedStatsPrinter.Print (records);

			// 6) Tails
			//WindowTailPrinter.PrintBlockTails (
			//	mornings,
			//	records,
			//	withSlBase,
			//	takeDays: 20,
			//	skipDays: 30,
			//	title: "Window tails (WITH SL)");

			// =====================================================================
			// 7) ANTI-DIRECTION OVERLAY (base/anti × with SL / no SL)
			// =====================================================================

			var withSlAnti = SimulateAllPolicies (
				policies, records, candles1m,
				useStopLoss: true,
				dailyStopPct,
				dailyTpPct,
				useAnti: true
			);

			var noSlAnti = SimulateAllPolicies (
				policies, records, candles1m,
				useStopLoss: false,
				dailyStopPct,
				dailyTpPct,
				useAnti: true
			);

			AntiDirectionComparisonPrinter.Print (
				withSlBase,
				withSlAnti,
				noSlBase,
				noSlAnti
			);
			}

		// =====================================================================
		// Бэктест для всех политик (с базовым или anti-direction режимом)
		// =====================================================================
		private static List<BacktestPolicyResult> SimulateAllPolicies (
			IReadOnlyList<PolicySpec> policies,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			bool useStopLoss,
			double dailyStopPct,
			double dailyTpPct,
			bool useAnti = false )
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
					out var hadLiquidation,
					useAntiDirectionOverlay: useAnti 
				);

				results.Add (new BacktestPolicyResult
					{
					PolicyName = p.Name,
					Margin = p.Margin,
					Trades = trades,
					TotalPnlPct = totalPnlPct,
					MaxDdPct = maxDdPct,
					WithdrawnTotal = withdrawnTotal,
					HadLiquidation = hadLiquidation,
					TradesBySource = tradesBySource,
					BucketSnapshots = bucketSnapshots
					});
				}

			return results
				.OrderBy (r => r.PolicyName)
				.ThenBy (r => r.Margin.ToString ())
				.ToList ();
			}
		}
	}
