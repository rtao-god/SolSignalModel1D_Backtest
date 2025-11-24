using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Core.Analytics.Reports
	{
	/// <summary>
	/// Строит компактный baseline-снапшот бэктеста на основе
	/// уже посчитанных BacktestPolicyResult (без пересчёта PnL).
	/// </summary>
	public static class BacktestBaselineSnapshotBuilder
		{
		/// <summary>
		/// Строит снапшот baseline-бэктеста.
		/// Ожидается, что withSlBase содержит прогон:
		/// - useStopLoss = true;
		/// - useAnti = false (baseline без overlay),
		/// но билдер технически работает с любыми режимами и может
		/// агрегировать base + anti, если туда передать объединённый список.
		/// </summary>
		public static BacktestBaselineSnapshot Build (
			IReadOnlyList<BacktestPolicyResult> withSlBase,
			double dailyStopPct,
			double dailyTpPct,
			string configName = "default" )
			{
			if (withSlBase == null) throw new ArgumentNullException (nameof (withSlBase));

			var generatedAtUtc = DateTime.UtcNow;

			// Генерируем простой, но уникальный Id: дата + время.
			// При желании формат можно ужесточить до "только дата".
			var id = $"backtest-baseline-{generatedAtUtc:yyyyMMdd_HHmmss}";

			var policySummaries = withSlBase
				.Select (r => new BacktestPolicySummary
					{
					PolicyName = r.PolicyName ?? string.Empty,
					MarginMode = r.Margin.ToString (),
					UseAntiDirectionOverlay = r.UseAntiDirectionOverlay,
					TotalPnlPct = r.TotalPnlPct,
					MaxDrawdownPct = r.MaxDdPct,
					HadLiquidation = r.HadLiquidation,
					WithdrawnTotal = r.WithdrawnTotal,
					TradesCount = r.Trades?.Count ?? 0
					})
				.ToArray ();

			return new BacktestBaselineSnapshot
				{
				Id = id,
				GeneratedAtUtc = generatedAtUtc,
				ConfigName = configName,
				DailyStopPct = dailyStopPct,
				DailyTpPct = dailyTpPct,
				Policies = policySummaries
				};
			}
		}
	}
