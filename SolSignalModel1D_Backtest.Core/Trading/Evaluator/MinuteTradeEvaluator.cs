using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.Trading.Evaluator
	{
	public static class MinuteTradeEvaluator
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

		public static HourlyTradeOutcome Evaluate (
			IReadOnlyList<Candle1m> day1m,
			DateTime entryUtc,
			bool goLong,
			bool goShort,
			double entryPrice,
			double dayMinMove,
			bool strongSignal,
			TimeZoneInfo nyTz )
			{
			if (nyTz == null)
				throw new ArgumentNullException (nameof (nyTz));
			if (day1m == null)
				throw new ArgumentNullException (nameof (day1m));
			if (entryPrice <= 0.0)
				throw new InvalidOperationException ("[minute-eval] entryPrice must be positive.");
			if (dayMinMove <= 0.0)
				{
				// Мин-движение — каузальный параметр. Если оно невалидно, “подкрутка” только скрывает проблему.
				throw new InvalidOperationException ($"[minute-eval] dayMinMove must be > 0. Got {dayMinMove:0.######}.");
				}

			var outcome = new HourlyTradeOutcome
				{
				Result = HourlyTradeResult.None,
				TpPct = 0.0,
				SlPct = 0.0
				};

			if (day1m.Count == 0)
				return outcome;

			// Контракт: ровно одно направление.
			if (goLong == goShort)
				{
				if (!goLong)
					return outcome;

				throw new InvalidOperationException ("[minute-eval] goLong and goShort cannot both be true.");
				}

			if (dayMinMove < MinDayTradeable)
				return outcome;

			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

			var window = day1m
				.Where (m => m.OpenTimeUtc >= entryUtc && m.OpenTimeUtc < exitUtc)
				.OrderBy (m => m.OpenTimeUtc)
				.ToList ();

			if (window.Count == 0)
				return outcome;

			double tpPct;
			double slPct;

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

			outcome.TpPct = tpPct;
			outcome.SlPct = slPct;

			double tpPrice;
			double slPrice;

			bool isLong = goLong;

			if (isLong)
				{
				tpPrice = entryPrice * (1.0 + tpPct);
				slPrice = entryPrice * (1.0 - slPct);
				}
			else
				{
				tpPrice = entryPrice * (1.0 - tpPct);
				slPrice = entryPrice * (1.0 + slPct);
				}

			foreach (var m in window)
				{
				if (isLong)
					{
					var tp = m.High >= tpPrice;
					var sl = m.Low <= slPrice;

					if (tp && sl) { outcome.Result = HourlyTradeResult.Ambiguous; return outcome; }
					if (tp) { outcome.Result = HourlyTradeResult.TpFirst; return outcome; }
					if (sl) { outcome.Result = HourlyTradeResult.SlFirst; return outcome; }
					}
				else
					{
					var tp = m.Low <= tpPrice;
					var sl = m.High >= slPrice;

					if (tp && sl) { outcome.Result = HourlyTradeResult.Ambiguous; return outcome; }
					if (tp) { outcome.Result = HourlyTradeResult.TpFirst; return outcome; }
					if (sl) { outcome.Result = HourlyTradeResult.SlFirst; return outcome; }
					}
				}

			return outcome;
			}
		}
	}
