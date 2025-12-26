using System;

namespace SolSignalModel1D_Backtest.Core.ML.Aggregation
	{
	/// <summary>
	/// Агрегатор вероятностей поверх дневной модели:
	/// 1) микро-слой (Day → Day+Micro),
	/// 2) SL-оверлей (Day+Micro → Total).
	///
	/// Ключевой контракт:
	/// - ApplyMicroOverlay НЕ имеет “нейтральных дефолтов”: если micro отсутствует, оверлей = identity (day без изменений).
	/// - Если micro.HasPrediction=true, то micro-вероятности обязаны быть конечными и валидными, иначе это ошибка пайплайна.
	/// </summary>
	internal static class ProbabilityAggregator
		{
		public static DailyProbabilities ApplyMicroOverlay (
			DailyProbabilities day,
			MicroProbabilities micro,
			ProbabilityAggregationConfig cfg )
			{
			if (cfg == null)
				throw new ArgumentNullException (nameof (cfg));

			ValidateDayDistribution (day, tag: "ApplyMicroOverlay");

			// Контракт оверлея: отсутствие микро-сигнала означает отсутствие воздействия (тождественное преобразование).
			// Это НЕ “заглушка”, а корректная математика: нельзя использовать несуществующий сигнал.
			if (!micro.HasPrediction)
				return day;

			ValidateMicroDistribution (micro);

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
				// Полная симметрия (0.5/0.5 или равные числа) → нет направленного сигнала.
				return day;
				}

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

		public static DailyProbabilities ApplySlOverlay (
			DailyProbabilities dayMicro,
			SlProbabilities sl,
			ProbabilityAggregationConfig cfg )
			{
			if (cfg == null)
				throw new ArgumentNullException (nameof (cfg));

			ValidateDayDistribution (dayMicro, tag: "ApplySlOverlay");

			double pUp = dayMicro.PUp;
			double pFlat = dayMicro.PFlat;
			double pDown = dayMicro.PDown;

			int baseTop = ArgmaxLabel (pUp, pFlat, pDown);

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

			double sum2 = pUp + pFlat + pDown;
			if (sum2 <= 0.0)
				{
				throw new InvalidOperationException (
					"[ProbabilityAggregator] ApplySlOverlay produced probabilities with non-positive sum.");
				}

			pUp /= sum2;
			pFlat /= sum2;
			pDown /= sum2;

			int newTop = ArgmaxLabel (pUp, pFlat, pDown);
			if (newTop != baseTop)
				{
				const double eps = 1e-6;

				double pBase, o1, o2;
				switch (baseTop)
					{
					case 2:
						pBase = pUp; o1 = pFlat; o2 = pDown;
						break;
					case 0:
						pBase = pDown; o1 = pUp; o2 = pFlat;
						break;
					default:
						pBase = pFlat; o1 = pUp; o2 = pDown;
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
							case 2: pUp = pBase; pFlat = o1; pDown = o2; break;
							case 0: pDown = pBase; pUp = o1; pFlat = o2; break;
							default: pFlat = pBase; pUp = o1; pDown = o2; break;
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

		private static void ValidateDayDistribution ( DailyProbabilities day, string tag )
			{
			double sum = day.PUp + day.PFlat + day.PDown;
			if (!double.IsFinite (sum) || sum <= 0.0)
				{
				throw new InvalidOperationException (
					$"[ProbabilityAggregator] {tag} received invalid day distribution: " +
					$"PUp={day.PUp}, PFlat={day.PFlat}, PDown={day.PDown}, sum={sum}");
				}
			}

		private static void ValidateMicroDistribution ( MicroProbabilities micro )
			{
			if (!double.IsFinite (micro.PUpGivenFlat) || !double.IsFinite (micro.PDownGivenFlat))
				{
				throw new InvalidOperationException (
					$"[ProbabilityAggregator] micro.HasPrediction=true but micro probs are not finite: " +
					$"PUpGivenFlat={micro.PUpGivenFlat}, PDownGivenFlat={micro.PDownGivenFlat}");
				}

			double sum = micro.PUpGivenFlat + micro.PDownGivenFlat;
			if (sum <= 0.0)
				{
				throw new InvalidOperationException (
					$"[ProbabilityAggregator] micro distribution sum <= 0: sum={sum}");
				}

			// Для бинарного микро ожидаем 1.0 (с разумной терпимостью к FP).
			if (Math.Abs (sum - 1.0) > 1e-6)
				{
				throw new InvalidOperationException (
					$"[ProbabilityAggregator] micro distribution sum != 1: sum={sum}, " +
					$"PUpGivenFlat={micro.PUpGivenFlat}, PDownGivenFlat={micro.PDownGivenFlat}");
				}

			if (!double.IsFinite (micro.Confidence) || micro.Confidence < 0.0 || micro.Confidence > 1.0)
				{
				throw new InvalidOperationException (
					$"[ProbabilityAggregator] invalid micro.Confidence={micro.Confidence}. Expected finite value in [0..1].");
				}
			}

		private static int ArgmaxLabel ( double pUp, double pFlat, double pDown )
			{
			if (pUp >= pFlat && pUp >= pDown) return 2;
			if (pDown >= pFlat && pDown >= pUp) return 0;
			return 1;
			}
		}
	}
