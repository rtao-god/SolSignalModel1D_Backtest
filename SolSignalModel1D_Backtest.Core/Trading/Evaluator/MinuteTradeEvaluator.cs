using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;
using System;
using System.Collections.Generic;

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
			bool strongSignal )
			{
			var outcome = new HourlyTradeOutcome
				{
				Result = HourlyTradeResult.None,
				TpPct = 0.0,
				SlPct = 0.0
				};

			if (day1m == null || day1m.Count == 0)
				return outcome;
			if (!goLong && !goShort)
				return outcome;

			if (dayMinMove <= 0)
				dayMinMove = 0.02;
			if (dayMinMove < MinDayTradeable)
				return outcome;

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

			outcome.TpPct = tpPct;
			outcome.SlPct = slPct;

			double tpPrice, slPrice;
			if (goLong)
				{
				tpPrice = entryPrice * (1.0 + tpPct);
				slPrice = entryPrice * (1.0 - slPct);
				}
			else
				{
				tpPrice = entryPrice * (1.0 - tpPct);
				slPrice = entryPrice * (1.0 + slPct);
				}

			foreach (var m in day1m)
				{
				if (goLong)
					{
					bool tp = m.High >= tpPrice;
					bool sl = m.Low <= slPrice;

					if (tp && sl)
						{
						outcome.Result = HourlyTradeResult.Ambiguous;
						return outcome;
						}
					if (tp)
						{
						outcome.Result = HourlyTradeResult.TpFirst;
						return outcome;
						}
					if (sl)
						{
						outcome.Result = HourlyTradeResult.SlFirst;
						return outcome;
						}
					}
				else
					{
					bool tp = m.Low <= tpPrice;
					bool sl = m.High >= slPrice;

					if (tp && sl)
						{
						outcome.Result = HourlyTradeResult.Ambiguous;
						return outcome;
						}
					if (tp)
						{
						outcome.Result = HourlyTradeResult.TpFirst;
						return outcome;
						}
					if (sl)
						{
						outcome.Result = HourlyTradeResult.SlFirst;
						return outcome;
						}
					}
				}

			return outcome;
			}
		}
	}
