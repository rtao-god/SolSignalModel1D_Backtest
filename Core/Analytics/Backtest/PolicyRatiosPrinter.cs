using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Метрики на уровне политики: Sharpe, Sortino, Calmar, WinRate, DD, Withdrawn и т.д.
	/// Возвращает и печатает табличку по всем политикам.
	/// </summary>
	public static class PolicyRatiosPrinter
		{
		private const double TotalCapitalUsd = 20000.0;
		private const int TradingDaysPerYear = 252;

		private sealed class Metrics
			{
			public int N;
			public double Mean;
			public double Std;
			public double DownStd;
			public double Sharpe;
			public double Sortino;
			public double MaxDd;
			public double Cagr;
			public double Calmar;
			public double WinRate;
			}

		public static void Print ( IEnumerable<BacktestPolicyResult> results, string title = "Policy ratios (Sharpe/Sortino/Calmar)" )
			{
			var list = results?.ToList () ?? new List<BacktestPolicyResult> ();
			if (list.Count == 0) return;

			ConsoleStyler.WriteHeader ($"=== {title} ===");

			var t = new TextTable ();
			t.AddHeader ("policy", "trades", "PnL %", "MaxDD %", "Sharpe", "Sortino", "Calmar", "WinRate %", "Withdrawn $", "Liq?");

			foreach (var r in list.OrderBy (x => x.PolicyName))
				{
				var m = ComputeMetrics (r);
				t.AddRow (
					r.PolicyName,
					r.Trades?.Count.ToString () ?? "0",
					r.TotalPnlPct.ToString ("0.00"),
					r.MaxDdPct.ToString ("0.00"),
					double.IsFinite (m.Sharpe) ? m.Sharpe.ToString ("0.00") : "—",
					double.IsFinite (m.Sortino) ? m.Sortino.ToString ("0.00") : "—",
					double.IsFinite (m.Calmar) ? m.Calmar.ToString ("0.00") : "—",
					(m.WinRate * 100.0).ToString ("0.0"),
					r.WithdrawnTotal.ToString ("0"),
					r.HadLiquidation ? "YES" : "no"
				);
				}

			t.WriteToConsole ();
			Console.WriteLine ();
			}

		private static Metrics ComputeMetrics ( BacktestPolicyResult r )
			{
			var res = new Metrics ();
			var trades = r.Trades ?? new List<PnLTrade> ();
			if (trades.Count == 0) return res;

			// перетаскиваем в хронологию
			trades = trades.OrderBy (x => x.EntryTimeUtc).ThenBy (x => x.ExitTimeUtc).ToList ();

			// ряд доходностей на весь капитал (per-trade)
			// r_i = (NetReturnPct/100) * (PositionUsd / TotalCapital)
			var rets = trades.Select (tr =>
			{
				double retOnPos = tr.NetReturnPct / 100.0;
				double weight = tr.PositionUsd / TotalCapitalUsd;
				return retOnPos * weight;
			}).ToList ();

			res.N = rets.Count;
			if (res.N == 0) return res;

			res.Mean = rets.Average ();
			res.Std = StdDev (rets);
			res.DownStd = StdDev (rets.Select (x => Math.Min (0.0, x)).ToList ());
			res.Sharpe = (res.Std > 1e-12) ? res.Mean / res.Std * Math.Sqrt (TradingDaysPerYear) : double.NaN;
			res.Sortino = (res.DownStd > 1e-12) ? res.Mean / res.DownStd * Math.Sqrt (TradingDaysPerYear) : double.NaN;

			// кривая капитала (мультипликативная)
			double eq = 1.0;
			double peak = 1.0;
			double maxDd = 0.0;
			foreach (var ri in rets)
				{
				eq *= (1.0 + ri);
				if (eq > peak) peak = eq;
				double dd = (peak - eq) / peak;
				if (dd > maxDd) maxDd = dd;
				}
			res.MaxDd = maxDd;

			// CAGR и Calmar (если считаем, что одна сделка ≈ 1 торговый день)
			double years = res.N / (double) TradingDaysPerYear;
			res.Cagr = years > 0 ? Math.Pow (eq, 1.0 / years) - 1.0 : 0.0;
			res.Calmar = maxDd > 1e-12 ? (res.Cagr / maxDd) : double.NaN;

			res.WinRate = trades.Count > 0
				? trades.Count (tr => tr.NetReturnPct > 0.0) / (double) trades.Count
				: 0.0;

			return res;
			}

		private static double StdDev ( IReadOnlyList<double> xs )
			{
			if (xs == null || xs.Count == 0) return 0.0;
			double mean = xs.Average ();
			double sum = 0.0;
			foreach (var v in xs) sum += (v - mean) * (v - mean);
			return Math.Sqrt (sum / xs.Count);
			}
		}
	}
