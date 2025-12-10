using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Analytics
	{
	public sealed class TradingReport
		{
		public double TotalPnlPct { get; set; }
		public double TotalPnlMultiplier { get; set; }
		public double MaxDrawdownPct { get; set; }
		public double Sharpe { get; set; }
		public double Sortino { get; set; }
		public double Calmar { get; set; }
		public int Trades { get; set; }
		public int TpHits { get; set; }
		public int TpTotal { get; set; }
		}

	public static class TradingMetrics
		{
		public static TradingReport Compute ( IEnumerable<BacktestRecord> records, double tpPct )
			{
			var recs = records.ToList ();

			var trades = recs
				.Where (r =>
					r.PredLabel == 0 || r.PredLabel == 2 ||
					(r.PredLabel == 1 && (r.PredMicroUp || r.PredMicroDown)))
				.OrderBy (r => r.DateUtc)
				.ToList ();

			double equity = 1.0;
			double peak = 1.0;
			double maxDD = 0.0;
			var rets = new List<double> ();

			int tpHits = 0;
			int tpTotal = 0;

			foreach (var t in trades)
				{
				bool isLong = t.PredLabel == 2 || (t.PredLabel == 1 && t.PredMicroUp);
				double dealRet;

				if (isLong)
					{
					double tpPrice = t.Entry * (1.0 + tpPct);
					if (t.MaxHigh24 >= tpPrice)
						{
						dealRet = tpPct;
						tpHits++;
						tpTotal++;
						}
					else
						{
						dealRet = (t.Close24 - t.Entry) / t.Entry;
						tpTotal++;
						}
					}
				else
					{
					double tpPrice = t.Entry * (1.0 - tpPct);
					if (t.MinLow24 <= tpPrice)
						{
						dealRet = tpPct;
						tpHits++;
						tpTotal++;
						}
					else
						{
						dealRet = (t.Entry - t.Close24) / t.Entry;
						tpTotal++;
						}
					}

				equity *= (1.0 + dealRet);
				rets.Add (dealRet);

				if (equity > peak) peak = equity;
				double dd = (peak - equity) / peak;
				if (dd > maxDD) maxDD = dd;
				}

			// Sharpe / Sortino
			double sharpe = 0.0;
			double sortino = 0.0;
			if (rets.Count > 1)
				{
				double avg = rets.Average ();
				double std = Math.Sqrt (rets.Sum (r => (r - avg) * (r - avg)) / (rets.Count - 1));
				if (std > 1e-9)
					sharpe = avg / std * Math.Sqrt (rets.Count);

				var neg = rets.Where (r => r < 0).ToList ();
				if (neg.Count > 0)
					{
					double negStd = Math.Sqrt (neg.Sum (r => r * r) / neg.Count);
					if (negStd > 1e-9)
						sortino = avg / negStd;
					}
				}

			double calmar = 0.0;
			if (maxDD > 1e-9)
				calmar = (equity - 1.0) / maxDD;

			return new TradingReport
				{
				TotalPnlPct = (equity - 1.0) * 100.0,
				TotalPnlMultiplier = equity,
				MaxDrawdownPct = maxDD * 100.0,
				Sharpe = sharpe,
				Sortino = sortino,
				Calmar = calmar,
				Trades = trades.Count,
				TpHits = tpHits,
				TpTotal = tpTotal
				};
			}
		}
	}
