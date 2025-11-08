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
		/// <summary>
		/// Часовой backtest: кто первый за день — TP или SL.
		/// TP/SL берём адаптивно от дневного MinMove, но:
		/// 1) слишком узкие дни вообще не торгуем (minMove &lt; 1.8%)
		/// 2) сильные и слабые сигналы имеют разные коэффициенты
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
				// направление
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				// если дневной порог очень маленький — пропускаем этот день,
				// потому что там много шума и SL будет ловиться часто
				double dayMinMove = rec.MinMove;
				if (dayMinMove <= 0)
					dayMinMove = 0.02; // фолбэк
				if (dayMinMove < 0.018)
					continue;

				DateTime start = rec.DateUtc;
				DateTime end = rec.DateUtc.AddHours (24);

				var dayBars = hours.Where (h => h.OpenTimeUtc >= start && h.OpenTimeUtc < end).ToList ();
				if (dayBars.Count == 0)
					continue;

				report.Trades++;

				// сильный сигнал или микро-сигнал
				bool strongSignal = rec.PredLabel == 0 || rec.PredLabel == 2;

				double tpPct;
				double slPct;

				if (strongSignal)
					{
					// для сильных: чуть больше TP, SL мягче
					tpPct = Math.Max (0.022, dayMinMove * 1.25);
					slPct = Math.Max (0.009, dayMinMove * 0.55);
					}
				else
					{
					// для боковика с наклоном: поменьше TP, но и SL поменьше
					tpPct = Math.Max (0.017, dayMinMove * 1.10);
					slPct = Math.Max (0.008, dayMinMove * 0.50);
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

				double closePrice = dayBars.Last ().Close;

				foreach (var bar in dayBars)
					{
					if (goLong)
						{
						bool tpInBar = bar.High >= tpPrice;
						bool slInBar = bar.Low <= slPrice;

						if (tpInBar && slInBar)
							{
							// внутри часа не знаем порядок
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

				// считаем результат
				double tradeRet;

				if (isAmb)
					{
					report.Ambiguous++;
					// не портим equity, просто пропускаем
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
					// ни TP, ни SL — закрываемся по последнему часу
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
