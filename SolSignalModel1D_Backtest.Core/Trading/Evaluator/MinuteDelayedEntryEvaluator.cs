using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Trading.Evaluator
	{
	public static class MinuteDelayedEntryEvaluator
		{
		private const double MinDayTradeable = 0.018;
		private const double StrongTpMul = 1.25;
		private const double StrongSlMul = 0.55;
		private const double WeakTpMul = 1.10;
		private const double WeakSlMul = 0.50;
		private const double StrongTpFloor = 0.022;
		private const double StrongSlFloor = 0.009;
		private const double WeakTpFloor = 0.017;
		private const double WeakSlFloor = 0.008;

		public sealed class MinuteDelayedOutcome
			{
			public bool Executed { get; set; }
			public DateTime? ExecutedAtUtc { get; set; }
			public double TargetEntryPrice { get; set; }
			public DelayedIntradayResult Result { get; set; } = DelayedIntradayResult.None;
			public double TpPct { get; set; }
			public double SlPct { get; set; }
			}

		/// <summary>
		/// Минутный вариант отложенного входа A:
		/// окно моделирования = [dayStartUtc; t_exit), где t_exit = ComputeBaselineExitUtc(dayStartUtc, nyTz).
		/// </summary>
		public static MinuteDelayedOutcome Evaluate (
			IReadOnlyList<Candle1m> day1m,
			DateTime dayStartUtc,
			bool goLong,
			bool goShort,
			double entryPrice12,
			double dayMinMove,
			bool strongSignal,
			double delayFactor,
			double maxDelayHours,
			TimeZoneInfo nyTz )
			{
			var res = new MinuteDelayedOutcome
				{
				Executed = false,
				TargetEntryPrice = entryPrice12,
				Result = DelayedIntradayResult.None
				};

			if (day1m == null || day1m.Count == 0)
				return res;
			if (!goLong && !goShort)
				return res;

			if (dayMinMove <= 0) dayMinMove = 0.02;
			if (dayMinMove < MinDayTradeable)
				return res;

			// новый baseline-горизонт вместо +24h
			DateTime exitUtc = Windowing.ComputeBaselineExitUtc (dayStartUtc, nyTz);

			var dayMinutes = day1m
				.Where (m => m.OpenTimeUtc >= dayStartUtc && m.OpenTimeUtc < exitUtc)
				.OrderBy (m => m.OpenTimeUtc)
				.ToList ();
			if (dayMinutes.Count == 0)
				return res;

			// желаемая цена
			double delayedPrice = goLong
				? entryPrice12 * (1.0 - delayFactor * dayMinMove)
				: entryPrice12 * (1.0 + delayFactor * dayMinMove);

			res.TargetEntryPrice = delayedPrice;

			// пробуем исполниться в первые maxDelayHours от dayStartUtc
			DateTime maxDelayUtc = dayStartUtc.AddHours (maxDelayHours);
			int fillIndex = -1;
			for (int i = 0; i < dayMinutes.Count; i++)
				{
				var m = dayMinutes[i];
				if (m.OpenTimeUtc > maxDelayUtc)
					break;

				bool filled = goLong
					? m.Low <= delayedPrice
					: m.High >= delayedPrice;

				if (filled)
					{
					fillIndex = i;
					break;
					}
				}

			if (fillIndex == -1)
				return res;

			res.Executed = true;
			res.ExecutedAtUtc = dayMinutes[fillIndex].OpenTimeUtc;

			// TP/SL
			double tpPct, slPct;
			if (strongSignal)
				{
				tpPct = Math.Max (StrongTpFloor, dayMinMove * StrongTpMul);
				slPct = Math.Max (StrongSlFloor, dayMinMove * StrongSlMul);
				}
			else
				{
				tpPct = Math.Max (WeakTpFloor, dayMinMove * WeakTpMul);
				slPct = Math.Max (WeakSlFloor, dayMinMove * WeakSlMul);
				}
			res.TpPct = tpPct;
			res.SlPct = slPct;

			double tpPrice = goLong
				? delayedPrice * (1.0 + tpPct)
				: delayedPrice * (1.0 - tpPct);

			double slPrice = goLong
				? delayedPrice * (1.0 - slPct)
				: delayedPrice * (1.0 + slPct);

			for (int i = fillIndex; i < dayMinutes.Count; i++)
				{
				var m = dayMinutes[i];

				if (goLong)
					{
					bool tp = m.High >= tpPrice;
					bool sl = m.Low <= slPrice;
					if (tp && sl)
						{
						res.Result = DelayedIntradayResult.Ambiguous;
						return res;
						}
					if (tp)
						{
						res.Result = DelayedIntradayResult.TpFirst;
						return res;
						}
					if (sl)
						{
						res.Result = DelayedIntradayResult.SlFirst;
						return res;
						}
					}
				else
					{
					bool tp = m.Low <= tpPrice;
					bool sl = m.High >= slPrice;
					if (tp && sl)
						{
						res.Result = DelayedIntradayResult.Ambiguous;
						return res;
						}
					if (tp)
						{
						res.Result = DelayedIntradayResult.TpFirst;
						return res;
						}
					if (sl)
						{
						res.Result = DelayedIntradayResult.SlFirst;
						return res;
						}
					}
				}

			res.Result = DelayedIntradayResult.None;
			return res;
			}

		public static DelayedEntryResult ToDelayedEntryResult ( this MinuteDelayedOutcome o )
			{
			return new DelayedEntryResult
				{
				Used = true,
				Executed = o.Executed,
				ExecutedAtUtc = o.ExecutedAtUtc,
				TargetEntryPrice = o.TargetEntryPrice,
				Result = o.Result,
				TpPct = o.TpPct,
				SlPct = o.SlPct
				};
			}
		}
	}
