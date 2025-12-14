using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
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
			IReadOnlyList<BacktestRecord> mornings,
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<PolicySpec> policies,
			BacktestConfig config )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (policies == null) throw new ArgumentNullException (nameof (policies));
			if (config == null) throw new ArgumentNullException (nameof (config));

			// 1) Базовые прогонки WITH SL / NO SL.
			var withSlBase = SimulateAllPolicies (
				policies,
				records,
				useStopLoss: true,
				config: config,
				useAnti: false
			);

			var noSlBase = SimulateAllPolicies (
				policies,
				records,
				useStopLoss: false,
				config: config,
				useAnti: false
			);

			// 2) Сравнение политик по SL.
			PolicySlComparisonPrinter.Print (withSlBase, noSlBase);

			// 3) Общие summary/ratios по WITH SL / NO SL.
			PolicyBreakdownPrinter.PrintSummary (withSlBase, "Policy summary (WITH SL)");
			PolicyBreakdownPrinter.PrintMonthlySkew (withSlBase, 12);

			PolicyRatiosPrinter.Print (withSlBase, "Policy ratios (WITH SL)");
			PolicyRatiosPrinter.Print (noSlBase, "Policy ratios (NO SL)");

			// 4) Anti-direction overlay (base/anti × with SL / no SL).
			var withSlAnti = SimulateAllPolicies (
				policies,
				records,
				useStopLoss: true,
				config: config,
				useAnti: true
			);

			var noSlAnti = SimulateAllPolicies (
				policies,
				records,
				useStopLoss: false,
				config: config,
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
		// Бэктест для всех политик (base/anti, with/without SL)
		// =====================================================================

		/// <summary>
		/// Считает PnL для всех политик при заданных:
		/// - флаге useStopLoss (дневной и intraday SL),
		/// - флаге useAnti (base vs anti-direction overlay),
		/// - BacktestConfig (TP/SL и прочее).
		/// </summary>
		public static List<BacktestPolicyResult> SimulateAllPolicies (
			IReadOnlyList<PolicySpec> policies,
			IReadOnlyList<BacktestRecord> records,
			bool useStopLoss,
			BacktestConfig config,
			bool useAnti = false )
			{
			if (policies == null) throw new ArgumentNullException (nameof (policies));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (config == null) throw new ArgumentNullException (nameof (config));

			var results = new List<BacktestPolicyResult> (policies.Count);

			foreach (var p in policies)
				{
				if (p.Policy == null) continue;

				PnlCalculator.ComputePnL (
					records,
					p.Policy,
					p.Margin,
					out var trades,
					out var totalPnlPct,
					out var maxDdPct,
					out var tradesBySource,
					out var withdrawnTotal,
					out var bucketSnapshots,
					out var hadLiquidation,
					useDailyStopLoss: useStopLoss,
					useDelayedIntradayStops: useStopLoss,
					dailyTpPct: config.DailyTpPct,
					dailyStopPct: config.DailyStopPct,
					useAntiDirectionOverlay: useAnti,
					predictionMode: PnlPredictionMode.DayOnly
				);

				results.Add (new BacktestPolicyResult
					{
					PolicyName = p.Name,
					Margin = p.Margin,
					UseAntiDirectionOverlay = useAnti,
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
