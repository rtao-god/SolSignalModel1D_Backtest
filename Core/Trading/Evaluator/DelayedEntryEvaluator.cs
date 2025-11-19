using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Trading.Evaluator
	{
	/// <summary>
	/// Логика отложенного (улучшенного) входа в тот же самый день.
	/// Не трогает ML, не трогает базовый HourlyTradeEvaluator.
	/// baseline-окно: [dayStartUtc; baselineExitUtc).
	/// </summary>
	public static class DelayedEntryEvaluator
		{
		// те же пороги, что и в обычном почасовом слое
		private const double MinDayTradeable = 0.018;
		private const double StrongTpMul = 1.25;
		private const double StrongSlMul = 0.55;
		private const double WeakTpMul = 1.10;
		private const double WeakSlMul = 0.50;
		private const double StrongTpFloor = 0.022;
		private const double StrongSlFloor = 0.009;
		private const double WeakTpFloor = 0.017;
		private const double WeakSlFloor = 0.008;

		/// <summary>
		/// Главный метод: попытаться войти лучше, чем в 12:00,
		/// живя в baseline-окне [dayStartUtc; baselineExitUtc).
		/// </summary>
		public static DelayedEntryResult Evaluate (
			IReadOnlyList<Candle1h> candles1h,
			DateTime dayStartUtc,
			bool goLong,
			bool goShort,
			double entryPrice12,
			double dayMinMove,
			bool strongSignal,
			double delayFactor,      // 0.3 / 0.5 / 1.0
			double maxDelayHours )    // обычно 4
			{
			var res = new DelayedEntryResult
				{
				Used = true,
				Executed = false,
				TargetEntryPrice = entryPrice12
				};

			if (candles1h == null || candles1h.Count == 0)
				return res;
			if (!goLong && !goShort)
				return res;

			if (dayMinMove <= 0)
				dayMinMove = 0.02;
			if (dayMinMove < MinDayTradeable)
				return res;

			// baseline-окно для delayed: то же, что и для дневных таргетов/PnL
			DateTime endUtc = Windowing.ComputeBaselineExitUtc (dayStartUtc);

			var dayBars = candles1h
				.Where (c => c.OpenTimeUtc >= dayStartUtc && c.OpenTimeUtc < endUtc)
				.ToList ();

			if (dayBars.Count == 0)
				return res;

			// на сколько хотим улучшить
			double delayedPrice = ComputeDelayedPrice (goLong, entryPrice12, dayMinMove, delayFactor);
			res.TargetEntryPrice = delayedPrice;

			// найдём час, в котором эту цену дали
			int hitIdx = FindFillIndex (dayBars, goLong, dayStartUtc, delayedPrice, maxDelayHours);
			if (hitIdx == -1)
				{
				// не исполнилось — так и пишем
				return res;
				}

			res.Executed = true;
			res.ExecutedAtUtc = dayBars[hitIdx].OpenTimeUtc;

			// посчитаем tp/sl проценты
			ComputeTpSl (dayMinMove, strongSignal, out double tpPct, out double slPct);
			res.TpPct = tpPct;
			res.SlPct = slPct;

			// уровни от новой цены
			ComputeLevels (goLong, delayedPrice, tpPct, slPct, out double tpPrice, out double slPrice);

			// теперь с этого часа идём вперёд и смотрим, что сработает
			for (int i = hitIdx; i < dayBars.Count; i++)
				{
				var bar = dayBars[i];

				if (goLong)
					{
					bool tp = bar.High >= tpPrice;
					bool sl = bar.Low <= slPrice;

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
					bool tp = bar.Low <= tpPrice;
					bool sl = bar.High >= slPrice;

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

			// ничего не сработало — intraday-результат None
			res.Result = DelayedIntradayResult.None;
			return res;
			}

		private static double ComputeDelayedPrice (
			bool goLong,
			double entryPrice,
			double dayMinMove,
			double delayFactor )
			{
			double shift = delayFactor * dayMinMove; // dayMinMove уже в долях
			if (goLong)
				return entryPrice * (1.0 - shift);
			else
				return entryPrice * (1.0 + shift);
			}

		private static int FindFillIndex (
			List<Candle1h> dayBars,
			bool goLong,
			DateTime dayStartUtc,
			double delayedPrice,
			double maxDelayHours )
			{
			for (int i = 0; i < dayBars.Count; i++)
				{
				var bar = dayBars[i];
				if ((bar.OpenTimeUtc - dayStartUtc).TotalHours > maxDelayHours)
					break;

				if (goLong)
					{
					if (bar.Low <= delayedPrice)
						return i;
					}
				else
					{
					if (bar.High >= delayedPrice)
						return i;
					}
				}

			return -1;
			}

		private static void ComputeTpSl (
			double dayMinMove,
			bool strongSignal,
			out double tpPct,
			out double slPct )
			{
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
			}

		private static void ComputeLevels (
			bool goLong,
			double entryPrice,
			double tpPct,
			double slPct,
			out double tpPrice,
			out double slPrice )
			{
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
			}
		}

	/// <summary>
	/// Сырые коды результата по отложенному входу,
	/// чтобы можно было просто int сохранить в PredictionRecord.
	/// </summary>
	public enum DelayedIntradayResult
		{
		None = 0,
		TpFirst = 1,
		SlFirst = 2,
		Ambiguous = 3
		}

	/// <summary>
	/// DTO для Program.cs — что случилось с попыткой улучшить вход.
	/// </summary>
	public sealed class DelayedEntryResult
		{
		public bool Used { get; set; }
		public bool Executed { get; set; }
		public DateTime? ExecutedAtUtc { get; set; }
		public double TargetEntryPrice { get; set; }

		public DelayedIntradayResult Result { get; set; } = DelayedIntradayResult.None;
		public double TpPct { get; set; }
		public double SlPct { get; set; }
		}
	}
