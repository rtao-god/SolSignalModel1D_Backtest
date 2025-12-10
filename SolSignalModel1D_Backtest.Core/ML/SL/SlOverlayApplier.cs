using System;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Применяет SL-оверлей к дневным вероятностям:
	/// на вход берутся Prob*_DayMicro и SlProb,
	/// на выходе обновляются Prob*_Total / PredLabel_Total / Conf_Sl*.
	/// </summary>
	public static class SlOverlayApplier
		{
		/// <summary>
		/// Модифицирует PredictionRecord in-place:
		/// - для дней без торгового сигнала оставляет Prob*_Total как есть;
		/// - для long/short-сигнала уменьшает вероятность стороны сделки
		///   пропорционально slProb и перераспределяет массу на Flat/противоположное направление;
		/// - обновляет PredLabel_Total и Conf_SlLong / Conf_SlShort.
		/// </summary>
		public static void Apply (
			PredictionRecord rec,
			double slProb,
			bool goLong,
			bool goShort,
			bool strongSignal )
			{
			if (rec == null) throw new ArgumentNullException (nameof (rec));

			// Для дней без торгового сигнала SL-оверлей не применяется.
			if (!goLong && !goShort)
				{
				return;
				}

			if (goLong && goShort)
				{
				throw new InvalidOperationException (
					$"[sl-overlay] Record {rec.DateUtc:O} has both goLong and goShort=true. " +
					"Ожидался не более чем один торговый сигнал.");
				}

			if (double.IsNaN (slProb) || slProb < 0.0 || slProb > 1.0)
				{
				throw new InvalidOperationException (
					$"[sl-overlay] Invalid SlProb={slProb} for date {rec.DateUtc:O}. " +
					"Ожидалось значение в диапазоне [0;1].");
				}

			double pUpBase = rec.ProbUp_DayMicro;
			double pFlatBase = rec.ProbFlat_DayMicro;
			double pDownBase = rec.ProbDown_DayMicro;

			if (pUpBase < 0.0 || pFlatBase < 0.0 || pDownBase < 0.0)
				{
				throw new InvalidOperationException (
					$"[sl-overlay] Negative DayMicro probability for date {rec.DateUtc:O}. " +
					$"P_up={pUpBase}, P_flat={pFlatBase}, P_down={pDownBase}.");
				}

			double sumBase = pUpBase + pFlatBase + pDownBase;
			if (sumBase <= 0.0)
				{
				throw new InvalidOperationException (
					$"[sl-overlay] Degenerate DayMicro triple (sum<=0) for date {rec.DateUtc:O}. " +
					$"P_up={pUpBase}, P_flat={pFlatBase}, P_down={pDownBase}.");
				}

			// Масштаб влияния SL: на "сильных" днях эффект больше, на слабых – мягче.
			double riskScale = strongSignal ? 1.0 : 0.6;
			double alpha = slProb * riskScale;

			if (alpha <= 0.0)
				{
				// SL ничего не добавляет к дневной информации – сохраняем DayMicro как Total.
				rec.ProbUp_Total = pUpBase;
				rec.ProbFlat_Total = pFlatBase;
				rec.ProbDown_Total = pDownBase;
				}
			else
				{
				if (alpha > 1.0) alpha = 1.0;

				// Целевая сторона – направление сделки (up для long, down для short).
				double pTargetBase;
				double pOther1Base;
				double pOther2Base;

				if (goLong)
					{
					pTargetBase = pUpBase;
					pOther1Base = pFlatBase;
					pOther2Base = pDownBase;
					}
				else
					{
					pTargetBase = pDownBase;
					pOther1Base = pFlatBase;
					pOther2Base = pUpBase;
					}

				double pTargetNew = pTargetBase * (1.0 - alpha);
				double delta = pTargetBase - pTargetNew;

				double pOther1New = pOther1Base;
				double pOther2New = pOther2Base;

				double othersSum = pOther1Base + pOther2Base;
				if (othersSum > 0.0)
					{
					double w1 = pOther1Base / othersSum;
					double w2 = pOther2Base / othersSum;

					pOther1New += delta * w1;
					pOther2New += delta * w2;
					}
				else
					{
					// Вся масса была в целевой стороне; переводим её в flat,
					// чтобы не делать полный разворот одним шагом.
					pOther1New += delta;
					pOther2New = 0.0;
					}

				double pUpNew;
				double pFlatNew;
				double pDownNew;

				if (goLong)
					{
					pUpNew = pTargetNew;
					pFlatNew = pOther1New;
					pDownNew = pOther2New;
					}
				else
					{
					pDownNew = pTargetNew;
					pFlatNew = pOther1New;
					pUpNew = pOther2New;
					}

				double sumNew = pUpNew + pFlatNew + pDownNew;
				if (sumNew <= 0.0)
					{
					throw new InvalidOperationException (
						$"[sl-overlay] SL overlay produced non-positive sum of probabilities for date {rec.DateUtc:O}.");
					}

				// Нормализация для численной устойчивости.
				pUpNew /= sumNew;
				pFlatNew /= sumNew;
				pDownNew /= sumNew;

				rec.ProbUp_Total = pUpNew;
				rec.ProbFlat_Total = pFlatNew;
				rec.ProbDown_Total = pDownNew;
				}

			// Пересчитываем итоговый класс Total (0=down,1=flat,2=up).
			int bestLabel = 0;
			double bestProb = rec.ProbDown_Total;

			if (rec.ProbFlat_Total > bestProb)
				{
				bestProb = rec.ProbFlat_Total;
				bestLabel = 1;
				}

			if (rec.ProbUp_Total > bestProb)
				{
				bestProb = rec.ProbUp_Total;
				bestLabel = 2;
				}

			rec.PredLabel_Total = bestLabel;

			// Конфиденсы SL по направлению.
			rec.Conf_SlLong = goLong ? slProb : 0.0;
			rec.Conf_SlShort = goShort ? slProb : 0.0;
			}
		}
	}
