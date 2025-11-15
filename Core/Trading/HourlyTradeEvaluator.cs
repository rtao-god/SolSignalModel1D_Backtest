using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.Trading
	{
	public sealed class HourlyTpSlReport
		{
		public double TotalPnlPct { get; set; }
		public double TotalPnlMultiplier { get; set; }
		public double MaxDrawdownPct { get; set; }

		public int Trades { get; set; }
		public int TpFirst { get; set; }
		public int SlFirst { get; set; }
		public int Ambiguous { get; set; }
		}

	public enum HourlyTradeResult
		{
		None = 0,
		TpFirst = 1,
		SlFirst = 2,
		Ambiguous = 3
		}

	public sealed class HourlyTradeOutcome
		{
		public HourlyTradeResult Result { get; set; }
		public double TpPct { get; set; }
		public double SlPct { get; set; }
		}

	public static class HourlyTradeEvaluator
		{
		// базовые коэффициенты
		private const double MinDayTradeable = 0.018; // дни с minMove < 1.8% — шум
		private const double StrongTpMul = 1.25;
		private const double StrongSlMul = 0.55;
		private const double WeakTpMul = 1.10;
		private const double WeakSlMul = 0.50;

		private const double StrongTpFloor = 0.022;
		private const double StrongSlFloor = 0.009;
		private const double WeakTpFloor = 0.017;
		private const double WeakSlFloor = 0.008;

		/// <summary>
		/// Простой “одиночный” расчёт: что случится в следующие 24 часа для заданного входа.
		/// </summary>
		public static HourlyTradeOutcome EvaluateOne (
			IReadOnlyList<Candle1h> candles1h,
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

			if (candles1h == null || candles1h.Count == 0)
				return outcome;

			if (!goLong && !goShort)
				return outcome;

			if (dayMinMove <= 0)
				dayMinMove = 0.02;

			if (dayMinMove < MinDayTradeable)
				return outcome;

			DateTime endUtc = entryUtc.AddHours (24);

			var dayBars = candles1h
				.Where (h => h.OpenTimeUtc >= entryUtc && h.OpenTimeUtc < endUtc)
				.OrderBy (h => h.OpenTimeUtc)
				.ToList ();

			if (dayBars.Count == 0)
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

			foreach (var bar in dayBars)
				{
				if (goLong)
					{
					bool tpInBar = bar.High >= tpPrice;
					bool slInBar = bar.Low <= slPrice;

					if (tpInBar && slInBar)
						{
						outcome.Result = HourlyTradeResult.Ambiguous;
						return outcome;
						}
					if (tpInBar)
						{
						outcome.Result = HourlyTradeResult.TpFirst;
						return outcome;
						}
					if (slInBar)
						{
						outcome.Result = HourlyTradeResult.SlFirst;
						return outcome;
						}
					}
				else
					{
					bool tpInBar = bar.Low <= tpPrice;
					bool slInBar = bar.High >= slPrice;

					if (tpInBar && slInBar)
						{
						outcome.Result = HourlyTradeResult.Ambiguous;
						return outcome;
						}
					if (tpInBar)
						{
						outcome.Result = HourlyTradeResult.TpFirst;
						return outcome;
						}
					if (slInBar)
						{
						outcome.Result = HourlyTradeResult.SlFirst;
						return outcome;
						}
					}
				}

			// ни TP ни SL за 24h
			outcome.Result = HourlyTradeResult.None;
			return outcome;
			}

		/// <summary>
		/// прогон по всем сделкам
		/// </summary>
		public static HourlyTpSlReport Evaluate (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1h> candles1h )
			{
			var report = new HourlyTpSlReport ();

			if (candles1h == null || candles1h.Count == 0)
				return report;

			var hours = candles1h.OrderBy (c => c.OpenTimeUtc).ToList ();

			double equity = 1.0;
			double peak = 1.0;
			double maxDd = 0.0;

			foreach (var rec in records.OrderBy (r => r.DateUtc))
				{
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				double dayMinMove = rec.MinMove;
				if (dayMinMove <= 0)
					dayMinMove = 0.02;
				if (dayMinMove < MinDayTradeable)
					continue;

				DateTime start = rec.DateUtc;
				DateTime end = rec.DateUtc.AddHours (24);

				var dayBars = hours.Where (h => h.OpenTimeUtc >= start && h.OpenTimeUtc < end).ToList ();
				if (dayBars.Count == 0)
					continue;

				report.Trades++;

				bool strongSignal = rec.PredLabel == 0 || rec.PredLabel == 2;

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

				double entry = rec.Entry;
				double tpPrice, slPrice;
				if (goLong)
					{
					tpPrice = entry * (1.0 + tpPct);
					slPrice = entry * (1.0 - slPct);
					}
				else
					{
					tpPrice = entry * (1.0 - tpPct);
					slPrice = entry * (1.0 + slPct);
					}

				bool hitTp = false;
				bool hitSl = false;
				bool isAmb = false;

				double closePrice = dayBars.Last ().Close;

				foreach (var bar in dayBars)
					{
					if (goLong)
						{
						bool tpInBar = bar.High >= tpPrice;
						bool slInBar = bar.Low <= slPrice;

						if (tpInBar && slInBar)
							{
							isAmb = true;
							break;
							}
						else if (tpInBar)
							{
							hitTp = true;
							break;
							}
						else if (slInBar)
							{
							hitSl = true;
							break;
							}
						}
					else
						{
						bool tpInBar = bar.Low <= tpPrice;
						bool slInBar = bar.High >= slPrice;

						if (tpInBar && slInBar)
							{
							isAmb = true;
							break;
							}
						else if (tpInBar)
							{
							hitTp = true;
							break;
							}
						else if (slInBar)
							{
							hitSl = true;
							break;
							}
						}
					}

				double tradeRet;
				if (isAmb)
					{
					report.Ambiguous++;
					continue;
					}
				else if (hitTp)
					{
					tradeRet = tpPct;
					report.TpFirst++;
					}
				else if (hitSl)
					{
					tradeRet = -slPct;
					report.SlFirst++;
					}
				else
					{
					if (goLong)
						tradeRet = (closePrice - entry) / entry;
					else
						tradeRet = (entry - closePrice) / entry;
					}

				equity *= (1.0 + tradeRet);
				if (equity > peak) peak = equity;
				double dd = (peak - equity) / peak;
				if (dd > maxDd) maxDd = dd;
				}

			report.TotalPnlMultiplier = equity;
			report.TotalPnlPct = (equity - 1.0) * 100.0;
			report.MaxDrawdownPct = maxDd * 100.0;

			return report;
			}
		}
	}
