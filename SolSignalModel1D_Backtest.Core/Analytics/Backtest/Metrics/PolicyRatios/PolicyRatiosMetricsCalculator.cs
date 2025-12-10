using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Metrics.PolicyRatios
	{
	public sealed class PolicyRatiosMetrics
		{
		public int TradesCount { get; init; }
		public double Mean { get; init; }
		public double Std { get; init; }
		public double DownStd { get; init; }
		public double Sharpe { get; init; }
		public double Sortino { get; init; }
		public double MaxDd { get; init; }
		public double Cagr { get; init; }
		public double Calmar { get; init; }
		public double WinRate { get; init; }
		}

	public static class PolicyRatiosMetricsCalculator
		{
		private const double TotalCapitalUsd = 20000.0;
		private const int TradingDaysPerYear = 252;

		public static PolicyRatiosMetrics Compute ( BacktestPolicyResult result )
			{
			if (result == null) throw new ArgumentNullException (nameof (result));

			var trades = result.Trades ?? new List<PnLTrade> ();
			if (trades.Count == 0)
				{
				return new PolicyRatiosMetrics
					{
					TradesCount = 0,
					Mean = 0.0,
					Std = 0.0,
					DownStd = 0.0,
					Sharpe = double.NaN,
					Sortino = double.NaN,
					MaxDd = 0.0,
					Cagr = 0.0,
					Calmar = double.NaN,
					WinRate = 0.0
					};
				}

			trades = trades
				.OrderBy (x => x.EntryTimeUtc)
				.ThenBy (x => x.ExitTimeUtc)
				.ToList ();

			var returns = trades.Select (tr =>
			{
				double retOnPos = tr.NetReturnPct / 100.0;
				double weight = tr.PositionUsd / TotalCapitalUsd;
				return retOnPos * weight;
			}).ToList ();

			int n = returns.Count;
			if (n == 0)
				{
				return new PolicyRatiosMetrics
					{
					TradesCount = 0,
					Mean = 0.0,
					Std = 0.0,
					DownStd = 0.0,
					Sharpe = double.NaN,
					Sortino = double.NaN,
					MaxDd = 0.0,
					Cagr = 0.0,
					Calmar = double.NaN,
					WinRate = 0.0
					};
				}

			double mean = returns.Average ();
			double std = StdDev (returns);
			double downStd = StdDev (returns.Select (x => Math.Min (0.0, x)).ToList ());

			double sharpe = std > 1e-12
				? mean / std * Math.Sqrt (TradingDaysPerYear)
				: double.NaN;

			double sortino = downStd > 1e-12
				? mean / downStd * Math.Sqrt (TradingDaysPerYear)
				: double.NaN;

			double eq = 1.0;
			double peak = 1.0;
			double maxDd = 0.0;

			foreach (var r in returns)
				{
				eq *= 1.0 + r;
				if (eq > peak) peak = eq;

				double dd = (peak - eq) / peak;
				if (dd > maxDd) maxDd = dd;
				}

			double years = n / (double) TradingDaysPerYear;
			double cagr = years > 0.0
				? Math.Pow (eq, 1.0 / years) - 1.0
				: 0.0;

			double calmar = maxDd > 1e-12
				? cagr / maxDd
				: double.NaN;

			double winRate = trades.Count > 0
				? trades.Count (tr => tr.NetReturnPct > 0.0) / (double) trades.Count
				: 0.0;

			return new PolicyRatiosMetrics
				{
				TradesCount = n,
				Mean = mean,
				Std = std,
				DownStd = downStd,
				Sharpe = sharpe,
				Sortino = sortino,
				MaxDd = maxDd,
				Cagr = cagr,
				Calmar = calmar,
				WinRate = winRate
				};
			}

		private static double StdDev ( IReadOnlyList<double> xs )
			{
			if (xs == null || xs.Count == 0) return 0.0;

			double mean = xs.Average ();
			double sum = 0.0;

			foreach (var v in xs)
				{
				double diff = v - mean;
				sum += diff * diff;
				}

			return Math.Sqrt (sum / xs.Count);
			}
		}
	}
