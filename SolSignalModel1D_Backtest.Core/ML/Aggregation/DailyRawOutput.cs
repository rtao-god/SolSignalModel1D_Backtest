using System;

namespace SolSignalModel1D_Backtest.Core.ML.Aggregation
	{
	/// <summary>
	/// Сырые выходы дневных моделей (move + dir) и флаги BTC-фильтра.
	/// Используется как промежуточный контейнер перед построением P_day.
	/// </summary>
	internal struct DailyRawOutput
		{
		/// <summary>
		/// Вероятность того, что день "с ходом" (move == true).
		/// Прямой вывод бинарной move-модели.
		/// </summary>
		public double PMove { get; set; }

		/// <summary>
		/// Условная вероятность "Up" при условии, что ход есть (move == true).
		/// Интерпретация: P(Up | Move).
		/// </summary>
		public double PUpGivenMove { get; set; }

		/// <summary>
		/// Флаг, что BTC-фильтр запрещает использовать сигнал "Up".
		/// Конкретные правила вычисления задаются на стороне PredictionEngine.
		/// </summary>
		public bool BtcFilterBlocksUp { get; set; }

		/// <summary>
		/// Резерв под возможные будущие правила блокировки flat.
		/// На текущем шаге не используется.
		/// </summary>
		public bool BtcFilterBlocksFlat { get; set; }

		/// <summary>
		/// Резерв под возможные будущие правила блокировки down.
		/// На текущем шаге не используется.
		/// </summary>
		public bool BtcFilterBlocksDown { get; set; }
		}

	/// <summary>
	/// Помощник, который из DailyRawOutput строит нормализованные дневные вероятности P_day.
	/// Формулы и BTC-оверлей локализованы здесь, чтобы PredictionEngine оставался тонким.
	/// </summary>
	internal static class DayProbabilityBuilder
		{
		public static DailyProbabilities BuildDayProbabilities ( DailyRawOutput raw )
			{
			// Базовый clamp, чтобы защититься от артефактов ML/float.
			double pMove = Clamp01 (raw.PMove);
			double pUpGivenMove = Clamp01 (raw.PUpGivenMove);

			// Базовые "сырые" вероятности без фильтров:
			// pFlat_raw = P(no-move),
			// pUp_raw / pDown_raw = разложение P(move) по направлению.
			double pFlatRaw = 1.0 - pMove;
			if (pFlatRaw < 0.0) pFlatRaw = 0.0;

			double pUpRaw = pMove * pUpGivenMove;
			double pDownRaw = pMove * (1.0 - pUpGivenMove);

			// Начальные дневные вероятности совпадают с сырыми.
			double pUpDay = pUpRaw;
			double pFlatDay = pFlatRaw;
			double pDownDay = pDownRaw;

			// ===== BTC-фильтр: блокировка up =====
			// Простое правило: если BTC блокирует long,
			// массу из up перекидываем во flat.
			if (raw.BtcFilterBlocksUp)
				{
				pFlatDay += pUpDay;
				pUpDay = 0.0;
				}

			// Можно добавить обработку BtcFilterBlocksFlat/BtcFilterBlocksDown при необходимости.

			// Нормализация на всякий случай, чтобы сумма была ≈ 1.
			double sum = pUpDay + pFlatDay + pDownDay;
			if (sum > 0.0)
				{
				pUpDay /= sum;
				pFlatDay /= sum;
				pDownDay /= sum;
				}

			double confidence = Math.Max (pUpDay, Math.Max (pFlatDay, pDownDay));

			return new DailyProbabilities
				{
				PUp = pUpDay,
				PFlat = pFlatDay,
				PDown = pDownDay,
				Confidence = confidence,
				BtcFilterBlockedUp = raw.BtcFilterBlocksUp,
				BtcFilterBlockedFlat = raw.BtcFilterBlocksFlat,
				BtcFilterBlockedDown = raw.BtcFilterBlocksDown
				};
			}

		private static double Clamp01 ( double value )
			{
			if (value < 0.0) return 0.0;
			if (value > 1.0) return 1.0;
			return value;
			}
		}
	}
