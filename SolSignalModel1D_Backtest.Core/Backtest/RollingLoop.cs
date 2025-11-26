using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
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
			BacktestConfig config )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));
			if (policies == null) throw new ArgumentNullException (nameof (policies));
			if (config == null) throw new ArgumentNullException (nameof (config));

			// 1) МИКРО-статистика
			MicroStatsPrinter.Print (mornings, records);

			// 2) Обычные прогонки WITH SL / NO SL
			var withSlBase = SimulateAllPolicies (
				policies, records, candles1m,
				useStopLoss: true,
				config: config,
				useAnti: false
			);

			var noSlBase = SimulateAllPolicies (
				policies, records, candles1m,
				useStopLoss: false,
				config: config,
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
				config: config,
				useAnti: true
			);

			var noSlAnti = SimulateAllPolicies (
				policies, records, candles1m,
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
		// Бэктест для всех политик (с базовым или anti-direction режимом)
		// =====================================================================

		/// <summary>
		/// Считает PnL для всех политик при заданных:
		/// - флаге useStopLoss (вкл/выкл дневной и intraday SL),
		/// - флаге useAnti (base vs anti-direction overlay),
		/// - BacktestConfig (SL/TP и прочее).
		///
		/// ВАЖНО:
		/// - метод теперь public, чтобы его можно было использовать
		///   вне RollingLoop (например, для baseline-снапшота);
		/// - логика внутри не меняется, только расширяется объект результата.
		/// </summary>
		public static List<BacktestPolicyResult> SimulateAllPolicies (
			IReadOnlyList<PolicySpec> policies,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			bool useStopLoss,
			BacktestConfig config,
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
					out var trades,
					out var totalPnlPct,
					out var maxDdPct,
					out var tradesBySource,
					out var withdrawnTotal,
					out var bucketSnapshots,
					out var hadLiquidation,
					useDailyStopLoss: useStopLoss,          // управляем дневным SL
					useDelayedIntradayStops: useStopLoss,   // и intraday SL для delayed тем же флагом
					dailyTpPct: config.DailyTpPct,          // TP берём из BacktestConfig
					dailyStopPct: config.DailyStopPct,      // SL берём из BacktestConfig
					useAntiDirectionOverlay: useAnti        // Anti-D
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
