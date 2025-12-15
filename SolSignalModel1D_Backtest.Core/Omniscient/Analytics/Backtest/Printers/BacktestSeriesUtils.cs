using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Вспомогательные методы для кривых: equity по дням, sharpe/sortino, maxDD.
	/// </summary>
	public static class BacktestSeriesUtils
		{
		public static SortedDictionary<DateTime, double> BuildDailyEquity (
			IList<PnLTrade> trades,
			double startEquity )
			{
			var dict = new SortedDictionary<DateTime, double> ();
			double last = startEquity;
			dict[DateTime.MinValue.ToCausalDateUtc()] = startEquity;

			foreach (var t in trades.OrderBy (t => t.DateUtc))
				{
				last = t.EquityAfter;
				dict[t.DateUtc.ToCausalDateUtc()] = last;
				}

			return dict;
			}

		public static (double sharpe, double sortino) ComputeSharpeSortino (
			SortedDictionary<DateTime, double> dailyEq )
			{
			var points = dailyEq
				.Where (kv => kv.Key != DateTime.MinValue.ToCausalDateUtc())
				.ToList ();

			if (points.Count < 2)
				return (0.0, 0.0);

			var rets = new List<double> ();
			double prev = points.First ().Value;

			for (int i = 1; i < points.Count; i++)
				{
				double cur = points[i].Value;
				if (prev > 0)
					{
					double r = (cur - prev) / prev;
					rets.Add (r);
					}
				prev = cur;
				}

			if (rets.Count == 0)
				return (0.0, 0.0);

			double avg = rets.Average ();
			double var = rets.Sum (r => (r - avg) * (r - avg)) / rets.Count;
			double std = Math.Sqrt (var);
			double sharpe = std > 1e-9 ? avg / std * Math.Sqrt (rets.Count) : 0.0;

			var neg = rets.Where (r => r < 0).ToList ();
			double sortino = 0.0;
			if (neg.Count > 0)
				{
				double negVar = neg.Sum (r => r * r) / neg.Count;
				double negStd = Math.Sqrt (negVar);
				if (negStd > 1e-9)
					sortino = avg / negStd;
				}

			return (sharpe, sortino);
			}

		public static double ComputeMaxDrawdownFromCurve ( SortedDictionary<DateTime, double> dailyEq )
			{
			double peak = 0.0;
			double maxDd = 0.0;
			foreach (var kv in dailyEq.Where (k => k.Key != DateTime.MinValue.ToCausalDateUtc()))
				{
				double v = kv.Value;
				if (v > peak) peak = v;
				if (peak > 0)
					{
					double dd = (peak - v) / peak;
					if (dd > maxDd) maxDd = dd;
					}
				}
			return maxDd * 100.0;
			}
		}
	}
