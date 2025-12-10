using System;

namespace SolSignalModel1D_Backtest.Core.ML.Aggregation
	{
	/// <summary>
	/// Агрегатор вероятностей поверх дневной модели:
	/// 1) микро-слой (Day → Day+Micro),
	/// 2) SL-оверлей (Day+Micro → Total).
	/// </summary>
	internal static class ProbabilityAggregator
		{
		/// <summary>
		/// Накладывает микро-оверлей на дневные вероятности.
		/// Day → Day+Micro.
		/// </summary>
		public static DailyProbabilities ApplyMicroOverlay (
			DailyProbabilities day,
			MicroProbabilities micro,
			ProbabilityAggregationConfig cfg )
			{
			if (cfg == null)
				throw new ArgumentNullException (nameof (cfg));

			// Sanity-check: базовые дневные вероятности должны образовывать валидное распределение.
			double daySum = day.PUp + day.PFlat + day.PDown;
			if (daySum <= 0.0)
				{
				throw new InvalidOperationException (
					"[ProbabilityAggregator] ApplyMicroOverlay received day probabilities with sum <= 0. " +
					$"PUp={day.PUp}, PFlat={day.PFlat}, PDown={day.PDown}");
				}

			// Нет микро-прогноза или низкая уверенность → ничего не меняем.
			if (!micro.HasPrediction)
				return day;

			if (micro.Confidence < cfg.MicroMinConfidence)
				return day;

			double pUp = day.PUp;
			double pFlat = day.PFlat;
			double pDown = day.PDown;

			// Направление микро: up или down.
			bool microUp;
			if (micro.PUpGivenFlat > micro.PDownGivenFlat)
				{
				microUp = true;
				}
			else if (micro.PDownGivenFlat > micro.PUpGivenFlat)
				{
				microUp = false;
				}
			else
				{
				// Симметрия по микро → нет смысла двигать.
				return day;
				}

			// Сила эффекта: зависит от уверенности микро и "сомнительности" дневной модели.
			double microSpan = Math.Max (1e-6, cfg.MicroStrongConfidence - cfg.MicroMinConfidence);
			double microStrength = (micro.Confidence - cfg.MicroMinConfidence) / microSpan;
			if (microStrength < 0.0) microStrength = 0.0;
			if (microStrength > 1.0) microStrength = 1.0;

			double dayFactor = 1.0 - Math.Max (0.0, Math.Min (1.0, day.Confidence));
			double beta = cfg.BetaMicro;
			if (beta < 0.0) beta = 0.0;

			double maxImpact = cfg.MaxMicroImpact * microStrength * dayFactor * beta;
			if (maxImpact <= 0.0)
				return day;

			const double eps = 1e-9;

			if (microUp)
				{
				double maxFromDown = Math.Max (0.0, pDown - eps);
				double maxFromFlat = Math.Max (0.0, pFlat - eps);

				if (maxFromDown <= 0.0 && maxFromFlat <= 0.0)
					return day;

				double desired = maxImpact;
				double takeFromDown = Math.Min (maxFromDown, desired * 0.7);
				double remaining = desired - takeFromDown;
				double takeFromFlat = Math.Min (maxFromFlat, remaining);
				double delta = takeFromDown + takeFromFlat;

				if (delta <= 0.0)
					return day;

				pUp += delta;
				pDown -= takeFromDown;
				pFlat -= takeFromFlat;
				}
			else
				{
				double maxFromUp = Math.Max (0.0, pUp - eps);
				double maxFromFlat = Math.Max (0.0, pFlat - eps);

				if (maxFromUp <= 0.0 && maxFromFlat <= 0.0)
					return day;

				double desired = maxImpact;
				double takeFromUp = Math.Min (maxFromUp, desired * 0.7);
				double remaining = desired - takeFromUp;
				double takeFromFlat = Math.Min (maxFromFlat, remaining);
				double delta = takeFromUp + takeFromFlat;

				if (delta <= 0.0)
					return day;

				pDown += delta;
				pUp -= takeFromUp;
				pFlat -= takeFromFlat;
				}

			// Нормализация результата: если сумма не положительна — это ошибка, а не повод молча вернуть day.
			double sum = pUp + pFlat + pDown;
			if (sum <= 0.0)
				{
				throw new InvalidOperationException (
					"[ProbabilityAggregator] ApplyMicroOverlay produced probabilities with non-positive sum.");
				}

			pUp /= sum;
			pFlat /= sum;
			pDown /= sum;

			double confidence = Math.Max (pUp, Math.Max (pFlat, pDown));

			return new DailyProbabilities
				{
				PUp = pUp,
				PFlat = pFlat,
				PDown = pDown,
				Confidence = confidence,
				BtcFilterBlockedUp = day.BtcFilterBlockedUp,
				BtcFilterBlockedFlat = day.BtcFilterBlockedFlat,
				BtcFilterBlockedDown = day.BtcFilterBlockedDown
				};
			}

		/// <summary>
		/// SL-оверлей поверх Day+Micro: учитывает риск SL для long/short
		/// и уменьшает привлекательность соответствующего направления.
		/// Day+Micro → Total.
		/// </summary>
		public static DailyProbabilities ApplySlOverlay (
			DailyProbabilities dayMicro,
			SlProbabilities sl,
			ProbabilityAggregationConfig cfg )
			{
			if (cfg == null)
				throw new ArgumentNullException (nameof (cfg));

			// Sanity-check: Day+Micro тоже должен быть валидным распределением.
			double baseSum = dayMicro.PUp + dayMicro.PFlat + dayMicro.PDown;
			if (baseSum <= 0.0)
				{
				throw new InvalidOperationException (
					"[ProbabilityAggregator] ApplySlOverlay received Day+Micro probabilities with sum <= 0. " +
					$"PUp={dayMicro.PUp}, PFlat={dayMicro.PFlat}, PDown={dayMicro.PDown}");
				}

			double pUp = dayMicro.PUp;
			double pFlat = dayMicro.PFlat;
			double pDown = dayMicro.PDown;

			// Базовый класс дня до SL-оверлея.
			int baseTop = ArgmaxLabel (pUp, pFlat, pDown);

			// ===== Риск для long (уменьшаем P_up) =====
			if (sl.ConfidenceLong >= cfg.SlMinConfidence && sl.PSlLong > 0.0)
				{
				double span = Math.Max (1e-6, cfg.SlStrongConfidence - cfg.SlMinConfidence);
				double confFactor = (sl.ConfidenceLong - cfg.SlMinConfidence) / span;
				if (confFactor < 0.0) confFactor = 0.0;
				if (confFactor > 1.0) confFactor = 1.0;

				double impact = cfg.GammaSl * cfg.MaxSlImpact * confFactor;
				if (impact > 0.0 && pUp > 0.0)
					{
					double reduceUp = Math.Min (impact, pUp);

					double toFlat = reduceUp * 0.6;
					double toDown = reduceUp - toFlat;

					pUp -= reduceUp;
					pFlat += toFlat;
					pDown += toDown;
					}
				}

			// ===== Риск для short (уменьшаем P_down) =====
			if (sl.ConfidenceShort >= cfg.SlMinConfidence && sl.PSlShort > 0.0)
				{
				double span = Math.Max (1e-6, cfg.SlStrongConfidence - cfg.SlMinConfidence);
				double confFactor = (sl.ConfidenceShort - cfg.SlMinConfidence) / span;
				if (confFactor < 0.0) confFactor = 0.0;
				if (confFactor > 1.0) confFactor = 1.0;

				double impact = cfg.GammaSl * cfg.MaxSlImpact * confFactor;
				if (impact > 0.0 && pDown > 0.0)
					{
					double reduceDown = Math.Min (impact, pDown);

					double toFlat = reduceDown * 0.6;
					double toUp = reduceDown - toFlat;

					pDown -= reduceDown;
					pFlat += toFlat;
					pUp += toUp;
					}
				}

			// Нормализация: если сумма получилась некорректной, это ошибка.
			double sum2 = pUp + pFlat + pDown;
			if (sum2 <= 0.0)
				{
				throw new InvalidOperationException (
					"[ProbabilityAggregator] ApplySlOverlay produced probabilities with non-positive sum.");
				}

			pUp /= sum2;
			pFlat /= sum2;
			pDown /= sum2;

			// Гарантия: класс дня не меняется.
			int newTop = ArgmaxLabel (pUp, pFlat, pDown);
			if (newTop != baseTop)
				{
				const double eps = 1e-6;

				double pBase, o1, o2;
				switch (baseTop)
					{
					case 2: // Up
						pBase = pUp;
						o1 = pFlat;
						o2 = pDown;
						break;
					case 0: // Down
						pBase = pDown;
						o1 = pUp;
						o2 = pFlat;
						break;
					default: // Flat
						pBase = pFlat;
						o1 = pUp;
						o2 = pDown;
						break;
					}

				double maxOther = Math.Max (o1, o2);
				if (pBase <= maxOther)
					{
					double delta = (maxOther + eps) - pBase;
					double othersSum = o1 + o2;

					if (othersSum > 0.0 && delta > 0.0 && delta < othersSum)
						{
						double k = delta / othersSum;
						o1 -= o1 * k;
						o2 -= o2 * k;
						pBase += delta;

						switch (baseTop)
							{
							case 2:
								pUp = pBase;
								pFlat = o1;
								pDown = o2;
								break;
							case 0:
								pDown = pBase;
								pUp = o1;
								pFlat = o2;
								break;
							default:
								pFlat = pBase;
								pUp = o1;
								pDown = o2;
								break;
							}

						double sum3 = pUp + pFlat + pDown;
						if (sum3 > 0.0)
							{
							pUp /= sum3;
							pFlat /= sum3;
							pDown /= sum3;
							}
						}
					}
				}

			double confidence = Math.Max (pUp, Math.Max (pFlat, pDown));

			return new DailyProbabilities
				{
				PUp = pUp,
				PFlat = pFlat,
				PDown = pDown,
				Confidence = confidence,
				BtcFilterBlockedUp = dayMicro.BtcFilterBlockedUp,
				BtcFilterBlockedFlat = dayMicro.BtcFilterBlockedFlat,
				BtcFilterBlockedDown = dayMicro.BtcFilterBlockedDown
				};
			}

		private static int ArgmaxLabel ( double pUp, double pFlat, double pDown )
			{
			if (pUp >= pFlat && pUp >= pDown) return 2;
			if (pDown >= pFlat && pDown >= pUp) return 0;
			return 1;
			}
		}
	}
