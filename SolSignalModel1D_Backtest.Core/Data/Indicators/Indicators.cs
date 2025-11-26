using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Indicators
	{
	/// <summary>
	/// Все расчётные индикаторы и утилиты под 6h-свечи в одном месте.
	/// Без заглядывания вперёд.
	/// </summary>
	public static class Indicators
		{
		// =========================
		// БАЗОВЫЕ УТИЛИТЫ
		// =========================

		/// <summary>
		/// Доходность за N окон назад: close[idx] / close[idx-N] - 1.
		/// </summary>
		public static double Ret6h ( IReadOnlyList<Candle6h> arr, int idx, int windowsBack )
			{
			if (arr == null || arr.Count == 0) return double.NaN;
			if (idx < 0 || idx >= arr.Count) return double.NaN;
			int prev = idx - windowsBack;
			if (prev < 0) return double.NaN;

			double now = arr[idx].Close;
			double past = arr[prev].Close;
			if (now <= 0 || past <= 0) return double.NaN;

			return now / past - 1.0;
			}

		/// <summary>
		/// Из словаря Date->value берёт точное значение, либо ближайшее ПРЕДЫДУЩЕЕ (шаг по умолчанию 6h, до maxBackSteps).
		/// </summary>
		public static double FindNearest ( Dictionary<DateTime, double> map, DateTime atUtc, double defaultValue, int maxBackSteps = 28, TimeSpan? step = null )
			{
			if (map == null || map.Count == 0) return defaultValue;
			if (map.TryGetValue (atUtc, out var exact)) return exact;

			var s = step ?? TimeSpan.FromHours (6);
			var d = atUtc;
			for (int i = 1; i <= maxBackSteps; i++)
				{
				d -= s;
				if (map.TryGetValue (d, out var v)) return v;
				}
			return defaultValue;
			}

		/// <summary>
		/// Короткая динамическая волатильность: средний |ret| за последние N окон (close-to-close).
		/// </summary>
		public static double ComputeDynVol6h ( IReadOnlyList<Candle6h> arr, int idx, int lookbackWindows )
			{
			if (arr == null || arr.Count == 0) return 0.0;
			int from = Math.Max (1, idx - lookbackWindows + 1);
			int to = idx;
			if (from >= to) return 0.0;

			double sumAbs = 0.0;
			int cnt = 0;
			for (int i = from; i <= to; i++)
				{
				double prev = arr[i - 1].Close;
				double cur = arr[i].Close;
				if (prev > 0 && cur > 0)
					{
					double ret = cur / prev - 1.0;
					sumAbs += Math.Abs (ret);
					cnt++;
					}
				}
			return cnt > 0 ? sumAbs / cnt : 0.0;
			}

		// =========================
		// ATR (Wilder)
		// =========================

		public static Dictionary<DateTime, double> ComputeAtr6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;

			double[] tr = new double[arr.Count];
			tr[0] = arr[0].High - arr[0].Low;
			for (int i = 1; i < arr.Count; i++)
				{
				double h = arr[i].High;
				double l = arr[i].Low;
				double pc = arr[i - 1].Close;
				double v1 = h - l;
				double v2 = Math.Abs (h - pc);
				double v3 = Math.Abs (l - pc);
				tr[i] = Math.Max (v1, Math.Max (v2, v3));
				}

			if (arr.Count < period) return res;

			double sum = 0.0;
			for (int i = 0; i < period; i++) sum += tr[i];
			double atr = sum / period;
			res[arr[period - 1].OpenTimeUtc] = atr;

			for (int i = period; i < arr.Count; i++)
				{
				atr = (atr * (period - 1) + tr[i]) / period;
				res[arr[i].OpenTimeUtc] = atr;
				}
			return res;
			}

		// =========================
		// RSI (Wilder)
		// =========================

		public static Dictionary<DateTime, double> ComputeRsi6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;

			double[] closes = arr.Select (c => c.Close).ToArray ();
			if (closes.Length < period + 1) return res;

			double gain = 0.0, loss = 0.0;
			for (int i = 1; i <= period; i++)
				{
				double diff = closes[i] - closes[i - 1];
				if (diff >= 0) gain += diff; else loss -= diff;
				}
			double avgGain = gain / period;
			double avgLoss = loss / period;

			res[arr[period].OpenTimeUtc] = ToRsi (avgGain, avgLoss);

			for (int i = period + 1; i < closes.Length; i++)
				{
				double diff = closes[i] - closes[i - 1];
				double g = diff > 0 ? diff : 0.0;
				double l = diff < 0 ? -diff : 0.0;

				avgGain = (avgGain * (period - 1) + g) / period;
				avgLoss = (avgLoss * (period - 1) + l) / period;

				res[arr[i].OpenTimeUtc] = ToRsi (avgGain, avgLoss);
				}
			return res;

			static double ToRsi ( double ag, double al )
				{
				if (al == 0.0) return ag == 0.0 ? 50.0 : 100.0;
				double rs = ag / al;
				return 100.0 - (100.0 / (1.0 + rs));
				}
			}

		/// <summary>
		/// Разница RSI(t) - RSI(t - days), опираясь на ближайшие ПРЕДЫДУЩИЕ точки.
		/// </summary>
		public static double GetRsiSlope6h ( Dictionary<DateTime, double> rsiMap, DateTime asOfOpenUtc, int days )
			{
			if (rsiMap == null || rsiMap.Count == 0) return 0.0;
			double now = FindNearest (rsiMap, asOfOpenUtc, double.NaN);
			if (double.IsNaN (now)) return 0.0;

			var pastTs = asOfOpenUtc.AddDays (-days);
			double past = FindNearest (rsiMap, pastTs, double.NaN);
			if (double.IsNaN (past)) return 0.0;

			return now - past;
			}

		// =========================
		// SMA / EMA
		// =========================

		public static Dictionary<DateTime, double> ComputeSma6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;

			double sum = 0.0;
			Queue<double> q = new Queue<double> (period);

			for (int i = 0; i < arr.Count; i++)
				{
				double v = arr[i].Close;
				sum += v;
				q.Enqueue (v);

				if (q.Count > period) sum -= q.Dequeue ();
				if (q.Count == period) res[arr[i].OpenTimeUtc] = sum / period;
				}
			return res;
			}

		public static Dictionary<DateTime, double> ComputeEma6h ( IReadOnlyList<Candle6h> arr, int period )
			{
			var res = new Dictionary<DateTime, double> ();
			if (arr == null || arr.Count == 0 || period <= 0) return res;
			if (arr.Count < period) return res;

			double sma = 0.0;
			for (int i = 0; i < period; i++) sma += arr[i].Close;
			sma /= period;

			double k = 2.0 / (period + 1);
			double ema = sma;
			res[arr[period - 1].OpenTimeUtc] = ema;

			for (int i = period; i < arr.Count; i++)
				{
				double close = arr[i].Close;
				ema = close * k + ema * (1.0 - k);
				res[arr[i].OpenTimeUtc] = ema;
				}
			return res;
			}

		// =========================
		// FNG / DXY вспомогательные
		// =========================

		/// <summary>
		/// Берём FNG на дату asOfUtcDate; если точной нет — ближайшая предыдущая (до 14 дней).
		/// Если нет, вернём 50.
		/// </summary>
		public static int PickNearestFng ( Dictionary<DateTime, int> fngByDate, DateTime asOfUtcDate )
			{
			if (fngByDate == null || fngByDate.Count == 0) return 50;
			if (fngByDate.TryGetValue (asOfUtcDate.Date, out int v)) return v;

			for (int i = 1; i <= 14; i++)
				{
				var d = asOfUtcDate.Date.AddDays (-i);
				if (fngByDate.TryGetValue (d, out v)) return v;
				}
			return 50;
			}

		/// <summary>
		/// Из dxySeries считаем изменение за 30 дней: DXY(t)/DXY(t-30d) - 1
		/// Используем ближайшие ПРЕДЫДУЩИЕ даты.
		/// </summary>
		public static double GetDxyChange30 ( Dictionary<DateTime, double> dxySeries, DateTime asOfUtcDate )
			{
			if (dxySeries == null || dxySeries.Count == 0) return 0.0;

			double now = FindNearest (dxySeries, asOfUtcDate.Date, double.NaN, maxBackSteps: 40, step: TimeSpan.FromDays (1));
			if (double.IsNaN (now)) return 0.0;

			double past = FindNearest (dxySeries, asOfUtcDate.Date.AddDays (-30), double.NaN, maxBackSteps: 45, step: TimeSpan.FromDays (1));
			if (double.IsNaN (past) || past <= 0) return 0.0;

			return now / past - 1.0;
			}
		}
	}
