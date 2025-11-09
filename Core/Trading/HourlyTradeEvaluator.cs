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

	public static class HourlyTradeEvaluator
		{
		// ====== НАСТРАИВАЕМОЕ ======

		// ниже этого дневного порога (из RowBuilder.MinMove) мы вообще не торгуем по часу
		private const double MinTradableDailyMove = 0.018;

		// для сильных сигналов (pred = 0/2): во сколько раз TP больше дневного minMove
		private const double StrongTpMul = 1.25;
		// для сильных сигналов: во сколько раз SL меньше дневного minMove (0.8 => SL дальше, чем было 0.55)
		private const double StrongSlMul = 0.80;

		// для слабых/микро-сигналов (pred = 1 + micro): чуть скромнее
		private const double WeakTpMul = 1.10;
		private const double WeakSlMul = 0.65;

		// жёсткие полы — чтобы в очень тихие дни не получилось 0.003% стопа
		private const double TpFloorStrong = 0.022;  // 2.2%
		private const double SlFloorStrong = 0.009;  // 0.9%
		private const double TpFloorWeak = 0.017;  // 1.7%
		private const double SlFloorWeak = 0.008;  // 0.8%

		/// <summary>
		/// Часовой backtest: кто первый за день — TP или SL.
		/// TP/SL берём адаптивно от дневного MinMove.
		/// </summary>
		public static HourlyTpSlReport Evaluate (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1h> candles1h )
			{
			var report = new HourlyTpSlReport ();

			if (candles1h == null || candles1h.Count == 0)
				return report;

			// на всякий сортируем
			var hours = candles1h.OrderBy (c => c.OpenTimeUtc).ToList ();

			double equity = 1.0;
			double peak = 1.0;
			double maxDd = 0.0;

			foreach (var rec in records.OrderBy (r => r.DateUtc))
				{
				// направление из прогноза
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				// дневной адаптивный порог
				double dayMinMove = rec.MinMove;
				if (dayMinMove <= 0)
					dayMinMove = 0.02; // fallback

				// слишком мелкие дни — пропускаем
				if (dayMinMove < MinTradableDailyMove)
					continue;

				DateTime start = rec.DateUtc;
				DateTime end = rec.DateUtc.AddHours (24);

				var dayBars = hours.Where (h => h.OpenTimeUtc >= start && h.OpenTimeUtc < end).ToList ();
				if (dayBars.Count == 0)
					continue;

				report.Trades++;

				// сильный сигнал — если модель дала 0 или 2
				bool strongSignal = rec.PredLabel == 0 || rec.PredLabel == 2;

				// считаем наши TP/SL в процентах
				double tpPct;
				double slPct;

				if (strongSignal)
					{
					tpPct = Math.Max (TpFloorStrong, dayMinMove * StrongTpMul);
					slPct = Math.Max (SlFloorStrong, dayMinMove * StrongSlMul);
					}
				else
					{
					tpPct = Math.Max (TpFloorWeak, dayMinMove * WeakTpMul);
					slPct = Math.Max (SlFloorWeak, dayMinMove * WeakSlMul);
					}

				double entry = rec.Entry;

				double tpPrice;
				double slPrice;

				if (goLong)
					{
					tpPrice = entry * (1.0 + tpPct);
					slPrice = entry * (1.0 - slPct);
					}
				else
					{
					// short
					tpPrice = entry * (1.0 - tpPct);
					slPrice = entry * (1.0 + slPct);
					}

				bool hitTp = false;
				bool hitSl = false;
				bool isAmb = false;

				// если ни то, ни то — выйдем по последнему часу
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
						// short
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
					// внутри часа сначала TP или SL — не знаем → пропустили
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
					// ни TP, ни SL — закрытие по последнему часу
					if (goLong)
						tradeRet = (closePrice - entry) / entry;
					else
						tradeRet = (entry - closePrice) / entry;
					}

				// учёт equity и просадки
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
