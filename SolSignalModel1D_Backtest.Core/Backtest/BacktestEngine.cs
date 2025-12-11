using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Универсальный движок бэктеста:
	/// принимает уже подготовленные данные и BacktestConfig,
	/// прогоняет все политики (BASE/ANTI-D × WITH SL / NO SL)
	/// через PnL-движок и возвращает BacktestSummary.
	/// </summary>
	public static class BacktestEngine
		{
		/// <summary>
		/// Выполняет one-shot бэктест по заданному конфигу и данным.
		/// Данные (mornings/records/candles1m/policies) ожидаются уже подготовленными.
		/// </summary>
		public static BacktestSummary RunBacktest (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			BacktestConfig config )
			{
			if (mornings == null || mornings.Count == 0)
				throw new ArgumentException ("mornings must be non-empty", nameof (mornings));
			if (records == null || records.Count == 0)
				throw new ArgumentException ("records must be non-empty", nameof (records));
			if (candles1m == null || candles1m.Count == 0)
				throw new ArgumentException ("candles1m must be non-empty", nameof (candles1m));
			if (policies == null || policies.Count == 0)
				throw new ArgumentException ("policies must be non-empty", nameof (policies));
			if (config == null)
				throw new ArgumentNullException (nameof (config));

			var fromDate = mornings.Min (r => r.Date);
			var toDate = mornings.Max (r => r.Date);

			// Тот же набор веток, что и в RollingLoop:
			// BASE/ANTI-D × WITH SL / NO SL.
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

			// Агрегаты по всем веткам и политикам.
			double bestTotalPnl = double.NegativeInfinity;
			double worstMaxDd = double.NegativeInfinity;
			int policiesWithLiq = 0;
			int totalTrades = 0;

			void Accumulate ( IEnumerable<BacktestPolicyResult> src )
				{
				foreach (var r in src)
					{
					if (r.Trades != null)
						totalTrades += r.Trades.Count;

					if (r.TotalPnlPct > bestTotalPnl)
						bestTotalPnl = r.TotalPnlPct;

					if (r.MaxDdPct > worstMaxDd)
						worstMaxDd = r.MaxDdPct;

					if (r.HadLiquidation)
						policiesWithLiq++;
					}
				}

			Accumulate (withSlBase);
			Accumulate (noSlBase);
			Accumulate (withSlAnti);
			Accumulate (noSlAnti);

			if (double.IsNegativeInfinity (bestTotalPnl))
				bestTotalPnl = 0.0;
			if (double.IsNegativeInfinity (worstMaxDd))
				worstMaxDd = 0.0;

			return new BacktestSummary
				{
				Config = config,
				FromDateUtc = fromDate,
				ToDateUtc = toDate,
				SignalDays = mornings.Count,
				WithSlBase = withSlBase,
				NoSlBase = noSlBase,
				WithSlAnti = withSlAnti,
				NoSlAnti = noSlAnti,
				BestTotalPnlPct = bestTotalPnl,
				WorstMaxDdPct = worstMaxDd,
				PoliciesWithLiquidation = policiesWithLiq,
				TotalTrades = totalTrades
				};
			}

		/// <summary>
		/// Локальный helper: прогоняет набор политик через PnL-движок
		/// для заданного режима (useStopLoss / useAnti).
		/// Логика полностью идентична RollingLoop.SimulateAllPolicies из рантайма.
		/// </summary>
		private static List<BacktestPolicyResult> SimulateAllPolicies (
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			bool useStopLoss,
			BacktestConfig config,
			bool useAnti )
			{
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
					Trades = trades,
					TotalPnlPct = totalPnlPct,
					MaxDdPct = maxDdPct,
					TradesBySource = tradesBySource,
					WithdrawnTotal = withdrawnTotal,
					BucketSnapshots = bucketSnapshots,
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
