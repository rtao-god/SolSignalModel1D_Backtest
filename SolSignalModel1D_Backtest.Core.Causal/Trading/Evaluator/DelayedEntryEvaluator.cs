using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator
	{
	public static class DelayedEntryEvaluator
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

		public static DelayedEntryResult Evaluate(
			IReadOnlyList<Candle1h> candles1h,
			DateTime dayStartUtc,
			bool goLong,
			bool goShort,
			double entryPrice12,
			double dayMinMove,
			bool strongSignal,
			double delayFactor,
			double maxDelayHours)
		{
			var res = new DelayedEntryResult
				{
				Used = true,
				Executed = false,
				TargetEntryPrice = entryPrice12
				};

			if (candles1h == null || candles1h.Count == 0)
				{
				throw new InvalidOperationException (
					"[DelayedEntryEvaluator] candles1h is null/empty: delayed-entry evaluation requires 1h bars.");
				}

			// Инвариант: направление должно быть ровно одно.
			if (goLong == goShort)
				throw new InvalidOperationException ("[DelayedEntryEvaluator] Invalid direction: expected goLong XOR goShort.");

			if (dayStartUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ("[DelayedEntryEvaluator] dayStartUtc must be UTC.");

			if (entryPrice12 <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (entryPrice12), entryPrice12, "[DelayedEntryEvaluator] entryPrice12 must be positive.");

			if (double.IsNaN (dayMinMove) || double.IsInfinity (dayMinMove) || dayMinMove <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (dayMinMove), dayMinMove, "[DelayedEntryEvaluator] dayMinMove must be finite and positive.");

			if (dayMinMove < MinDayTradeable)
				return res;

			if (double.IsNaN (delayFactor) || double.IsInfinity (delayFactor) || delayFactor <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (delayFactor), delayFactor, "[DelayedEntryEvaluator] delayFactor must be finite and positive.");

			if (double.IsNaN (maxDelayHours) || double.IsInfinity (maxDelayHours) || maxDelayHours <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (maxDelayHours), maxDelayHours, "[DelayedEntryEvaluator] maxDelayHours must be finite and positive.");

			DateTime endUtc = NyWindowing.ComputeBaselineExitUtc(new EntryUtc(dayStartUtc), NyWindowing.NyTz).Value;

			var dayBars = candles1h
				.Where (c => c.OpenTimeUtc >= dayStartUtc && c.OpenTimeUtc < endUtc)
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			if (dayBars.Count == 0)
				return res;

			double delayedPrice = ComputeDelayedPrice (goLong, entryPrice12, dayMinMove, delayFactor);
			res.TargetEntryPrice = delayedPrice;

			int hitIdx = FindFillIndex (dayBars, goLong, dayStartUtc, delayedPrice, maxDelayHours);
			if (hitIdx == -1)
				return res;

			res.Executed = true;
			res.ExecutedAtUtc = dayBars[hitIdx].OpenTimeUtc;

			ComputeTpSl (dayMinMove, strongSignal, out double tpPct, out double slPct);
			res.TpPct = tpPct;
			res.SlPct = slPct;

			ComputeLevels (goLong, delayedPrice, tpPct, slPct, out double tpPrice, out double slPrice);

			for (int i = hitIdx; i < dayBars.Count; i++)
				{
				var bar = dayBars[i];

				if (goLong)
					{
					bool tp = bar.High >= tpPrice;
					bool sl = bar.Low <= slPrice;

					if (tp && sl) { res.Result = DelayedIntradayResult.Ambiguous; return res; }
					if (tp) { res.Result = DelayedIntradayResult.TpFirst; return res; }
					if (sl) { res.Result = DelayedIntradayResult.SlFirst; return res; }
					}
				else
					{
					bool tp = bar.Low <= tpPrice;
					bool sl = bar.High >= slPrice;

					if (tp && sl) { res.Result = DelayedIntradayResult.Ambiguous; return res; }
					if (tp) { res.Result = DelayedIntradayResult.TpFirst; return res; }
					if (sl) { res.Result = DelayedIntradayResult.SlFirst; return res; }
					}
				}

			res.Result = DelayedIntradayResult.None;
			return res;
			}

		private static double ComputeDelayedPrice ( bool goLong, double entryPrice, double dayMinMove, double delayFactor )
			{
			double shift = delayFactor * dayMinMove;
			return goLong ? entryPrice * (1.0 - shift) : entryPrice * (1.0 + shift);
			}

		private static int FindFillIndex ( List<Candle1h> dayBars, bool goLong, DateTime dayStartUtc, double delayedPrice, double maxDelayHours )
			{
			for (int i = 0; i < dayBars.Count; i++)
				{
				var bar = dayBars[i];
				if ((bar.OpenTimeUtc - dayStartUtc).TotalHours > maxDelayHours)
					break;

				if (goLong)
					{
					if (bar.Low <= delayedPrice) return i;
					}
				else
					{
					if (bar.High >= delayedPrice) return i;
					}
				}

			return -1;
			}

		private static void ComputeTpSl ( double dayMinMove, bool strongSignal, out double tpPct, out double slPct )
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

		private static void ComputeLevels ( bool goLong, double entryPrice, double tpPct, double slPct, out double tpPrice, out double slPrice )
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

	public enum DelayedIntradayResult
		{
		None = 0,
		TpFirst = 1,
		SlFirst = 2,
		Ambiguous = 3
		}

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

