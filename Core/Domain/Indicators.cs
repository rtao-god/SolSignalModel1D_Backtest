using SolSignalModel1D_Backtest.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Domain
	{
	public static class Indicators
		{
		public static Dictionary<DateTime, double> ComputeAtr6h ( List<Candle6h> series, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (series.Count < period + 1) return res;
			double prevClose = series[0].Close;
			double trSum = 0;
			for (int i = 1; i <= period; i++)
				{
				double tr = TrueRange (series[i].High, series[i].Low, prevClose);
				trSum += tr;
				prevClose = series[i].Close;
				}
			double atr = trSum / period;
			res[series[period].OpenTimeUtc] = atr;
			for (int i = period + 1; i < series.Count; i++)
				{
				double tr = TrueRange (series[i].High, series[i].Low, prevClose);
				atr = (atr * (period - 1) + tr) / period;
				res[series[i].OpenTimeUtc] = atr;
				prevClose = series[i].Close;
				}
			return res;
			}

		private static double TrueRange ( double high, double low, double prevClose )
			{
			double tr1 = high - low;
			double tr2 = Math.Abs (high - prevClose);
			double tr3 = Math.Abs (low - prevClose);
			return Math.Max (tr1, Math.Max (tr2, tr3));
			}

		public static Dictionary<DateTime, double> ComputeSma6h ( List<Candle6h> series, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (series.Count < period) return res;
			double sum = 0;
			for (int i = 0; i < period; i++) sum += series[i].Close;
			res[series[period - 1].OpenTimeUtc] = sum / period;
			for (int i = period; i < series.Count; i++)
				{
				sum += series[i].Close - series[i - period].Close;
				res[series[i].OpenTimeUtc] = sum / period;
				}
			return res;
			}

		public static Dictionary<DateTime, double> ComputeRsi6h ( List<Candle6h> series, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (series.Count < period + 1) return res;
			double gain = 0, loss = 0;
			for (int i = 1; i <= period; i++)
				{
				double diff = series[i].Close - series[i - 1].Close;
				if (diff >= 0) gain += diff;
				else loss -= diff;
				}
			double avgGain = gain / period;
			double avgLoss = loss / period;
			double rs = avgLoss == 0 ? 0 : avgGain / avgLoss;
			double rsi = avgLoss == 0 ? 100 : 100 - 100 / (1 + rs);
			res[series[period].OpenTimeUtc] = rsi;
			for (int i = period + 1; i < series.Count; i++)
				{
				double diff = series[i].Close - series[i - 1].Close;
				double g = diff > 0 ? diff : 0;
				double l = diff < 0 ? -diff : 0;
				avgGain = (avgGain * (period - 1) + g) / period;
				avgLoss = (avgLoss * (period - 1) + l) / period;
				rs = avgLoss == 0 ? 0 : avgGain / avgLoss;
				rsi = avgLoss == 0 ? 100 : 100 - 100 / (1 + rs);
				res[series[i].OpenTimeUtc] = rsi;
				}
			return res;
			}

		public static double GetRsiSlope6h ( Dictionary<DateTime, double> rsi, DateTime d, int windowsBack )
			{
			if (!rsi.TryGetValue (d, out double now)) return 0.0;
			DateTime back = d.AddHours (-6 * windowsBack);
			double prev = FindNearest (rsi, back);
			if (Math.Abs (prev) < 1e-9) return 0.0;
			return (now - prev) / Math.Abs (prev) * 100.0;
			}

		public static double Ret6h ( List<Candle6h> series, int idx, int windowsBack )
			{
			int prev = idx - windowsBack;
			if (prev < 0) return double.NaN;
			double now = series[idx].Close;
			double before = series[prev].Close;
			if (before <= 0) return double.NaN;
			return now / before - 1.0;
			}

		public static double ComputeDynVol6h ( List<Candle6h> series, int idx, int window )
			{
			int start = idx - window;
			if (start < 1) return 0.0;
			double sum = 0;
			int cnt = 0;
			for (int i = start; i <= idx; i++)
				{
				double r = Ret6h (series, i, 1);
				if (double.IsNaN (r)) continue;
				sum += Math.Abs (r);
				cnt++;
				}
			if (cnt == 0) return 0.0;
			return sum / cnt;
			}

		public static double FindNearest ( Dictionary<DateTime, double> dict, DateTime d, double def = 0.0 )
			{
			double best = double.NaN;
			double bestDiff = double.MaxValue;
			foreach (var kv in dict)
				{
				double diff = Math.Abs ((kv.Key - d).TotalSeconds);
				if (diff < bestDiff)
					{
					bestDiff = diff;
					best = kv.Value;
					}
				}
			return double.IsNaN (best) ? def : best;
			}

		public static int PickNearestFng ( Dictionary<DateTime, int> fng, DateTime target )
			{
			int best = 50;
			double bestDiff = double.MaxValue;
			foreach (var kv in fng)
				{
				double diff = Math.Abs ((kv.Key - target).TotalDays);
				if (diff < bestDiff)
					{
					bestDiff = diff;
					best = kv.Value;
					}
				}
			return best;
			}

		public static double GetDxyChange30 ( Dictionary<DateTime, double> dxy, DateTime d )
			{
			double now = FindNearest (dxy, d);
			double ago = FindNearest (dxy, d.AddDays (-30));
			if (now > 0 && ago > 0) return now / ago - 1.0;
			return 0.0;
			}
		public static Dictionary<DateTime, double> ComputeEma6h ( List<Candle6h> candles, int period )
			{
			var result = new Dictionary<DateTime, double> ();
			if (candles == null || candles.Count == 0)
				return result;

			// сортируем на всякий
			var ordered = candles.OrderBy (c => c.OpenTimeUtc).ToList ();

			double k = 2.0 / (period + 1.0);
			double? prevEma = null;

			foreach (var c in ordered)
				{
				double price = c.Close;
				if (price <= 0)
					continue;

				double ema;
				if (prevEma == null)
					ema = price;              // старт — с цены
				else
					ema = price * k + prevEma.Value * (1.0 - k);

				result[c.OpenTimeUtc] = ema;
				prevEma = ema;
				}

			return result;
			}

		}
	}