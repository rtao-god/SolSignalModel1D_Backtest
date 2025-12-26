using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
	{
	/// <summary>
	/// Вспомогательные индикаторные расчеты для стратегий.
	/// Здесь:
	/// - ресэмпл 1m в 15m;
	/// - RSI по 15m закрытиям;
	/// - поиск RSI на момент времени.
	/// </summary>
	public static class StrategyIndicatorUtils
		{
		public sealed class RsiPoint
			{
			public DateTime TimeUtc { get; init; }
			public double Value { get; init; }
			}

		private sealed class FifteenMinuteBar
			{
			public DateTime EndTimeUtc { get; init; }
			public double Close { get; init; }
			}

		/// <summary>
		/// Строит 15m бары по 1m свечам и считает RSI по их закрытиям.
		/// Возвращает список точек (время конца 15m бара, RSI).
		/// </summary>
		public static List<RsiPoint> Build15mRsi ( IReadOnlyList<Candle1m> candles1m, int period )
			{
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));
			if (period <= 0) throw new ArgumentOutOfRangeException (nameof (period));

			if (candles1m.Count == 0)
				return new List<RsiPoint> ();

			var ordered = candles1m
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			var bars = new List<FifteenMinuteBar> ();

			var firstTime = ordered[0].OpenTimeUtc;
			DateTime bucketStart = AlignTo15m (firstTime);
			DateTime bucketEnd = bucketStart.AddMinutes (15);

			double lastClose = ordered[0].Close;

			foreach (var c in ordered)
				{
				while (c.OpenTimeUtc >= bucketEnd)
					{
					bars.Add (new FifteenMinuteBar
						{
						EndTimeUtc = bucketEnd,
						Close = lastClose
						});

					bucketStart = bucketEnd;
					bucketEnd = bucketStart.AddMinutes (15);
					}

				lastClose = c.Close;
				}

			// Закрываем последний незавершенный интервал
			bars.Add (new FifteenMinuteBar
				{
				EndTimeUtc = bucketEnd,
				Close = lastClose
				});

			if (bars.Count <= period)
				return new List<RsiPoint> ();

			var rsi = new List<RsiPoint> (bars.Count - period);

			double avgGain = 0.0;
			double avgLoss = 0.0;

			// Первое среднее по period шагам
			for (int i = 1; i <= period; i++)
				{
				double diff = bars[i].Close - bars[i - 1].Close;
				if (diff > 0)
					avgGain += diff;
				else
					avgLoss -= diff;
				}

			avgGain /= period;
			avgLoss /= period;

			// Wilder RSI
			for (int i = period + 1; i < bars.Count; i++)
				{
				double diff = bars[i].Close - bars[i - 1].Close;
				double gain = diff > 0 ? diff : 0.0;
				double loss = diff < 0 ? -diff : 0.0;

				avgGain = (avgGain * (period - 1) + gain) / period;
				avgLoss = (avgLoss * (period - 1) + loss) / period;

				double rsiValue;

				if (avgLoss == 0.0)
					{
					rsiValue = 100.0;
					}
				else
					{
					double rs = avgGain / avgLoss;
					rsiValue = 100.0 - 100.0 / (1.0 + rs);
					}

				rsi.Add (new RsiPoint
					{
					TimeUtc = bars[i].EndTimeUtc,
					Value = rsiValue
					});
				}

			return rsi;
			}

		private static DateTime AlignTo15m ( DateTime t )
			{
			int minuteBucket = (t.Minute / 15) * 15;
			return new DateTime (t.Year, t.Month, t.Day, t.Hour, minuteBucket, 0, DateTimeKind.Utc);
			}

		/// <summary>
		/// Находит последнее значение RSI, чье время <= timeUtc.
		/// Если такой точки нет или серия пустая, вернет null.
		/// </summary>
		public static double? GetRsiAt ( IReadOnlyList<RsiPoint> series, DateTime timeUtc )
			{
			if (series == null) throw new ArgumentNullException (nameof (series));
			if (series.Count == 0) return null;

			int lo = 0;
			int hi = series.Count - 1;
			int best = -1;

			while (lo <= hi)
				{
				int mid = lo + (hi - lo) / 2;
				if (series[mid].TimeUtc <= timeUtc)
					{
					best = mid;
					lo = mid + 1;
					}
				else
					{
					hi = mid - 1;
					}
				}

			if (best < 0)
				return null;

			return series[best].Value;
			}
		}
	}
