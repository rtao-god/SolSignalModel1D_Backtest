using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.SL
	{
	/// <summary>
	/// Runtime-оверлей SL: адаптирует CausalPredictionRecord к ProbabilityAggregator.
	/// Вся математика перераспределения вероятностей лежит в ProbabilityAggregator.ApplySlOverlay.
	/// </summary>
	public static class SlOverlayApplier
		{
		private static readonly ProbabilityAggregationConfig Config =
			new ProbabilityAggregationConfig ();

		public static void Apply (
			CausalPredictionRecord rec,
			double slProb,
			bool goLong,
			bool goShort,
			bool strongSignal )
			{
			if (rec == null) throw new ArgumentNullException (nameof (rec));

			// Без торгового сигнала SL-оверлей не нужен.
			if (!goLong && !goShort)
				return;

			if (goLong && goShort)
				{
				throw new InvalidOperationException (
					$"[sl-overlay] record {rec.DateUtc:O} has both goLong and goShort=true. " +
					"Ожидался не более чем один торговый сигнал.");
				}

			if (double.IsNaN (slProb) || slProb < 0.0 || slProb > 1.0)
				{
				throw new InvalidOperationException (
					$"[sl-overlay] Invalid slProb={slProb} for date {rec.DateUtc:O}. " +
					"Ожидалось значение в диапазоне [0;1].");
				}

			// Базовые вероятности Day+Micro до SL-оверлея.
			var dayMicro = new DailyProbabilities
				{
				PUp = rec.ProbUp_DayMicro,
				PFlat = rec.ProbFlat_DayMicro,
				PDown = rec.ProbDown_DayMicro,
				Confidence = Math.Max (
					rec.ProbUp_DayMicro,
					Math.Max (rec.ProbFlat_DayMicro, rec.ProbDown_DayMicro)),
				BtcFilterBlockedUp = false,
				BtcFilterBlockedFlat = false,
				BtcFilterBlockedDown = false
				};

			// Риск-слой SL: одна вероятность slProb для активного направления.
			// Используем её и как риск, и как confidence.
			var sl = new SlProbabilities
				{
				HasPrediction = true,
				PSlLong = goLong ? slProb : 0.0,
				PSlShort = goShort ? slProb : 0.0,
				ConfidenceLong = goLong ? slProb : 0.0,
				ConfidenceShort = goShort ? slProb : 0.0
				};

			var total = ProbabilityAggregator.ApplySlOverlay (dayMicro, sl, Config);

			rec.ProbUp_Total = total.PUp;
			rec.ProbFlat_Total = total.PFlat;
			rec.ProbDown_Total = total.PDown;

			rec.PredLabel_Total = ArgmaxLabel (total.PUp, total.PFlat, total.PDown);

			// Сами конфиденсы SL по направлениям всё так же торчат на уровне causal-рекорда.
			rec.Conf_SlLong = goLong ? slProb : 0.0;
			rec.Conf_SlShort = goShort ? slProb : 0.0;
			}

		private static int ArgmaxLabel ( double pUp, double pFlat, double pDown )
			{
			if (pUp >= pFlat && pUp >= pDown) return 2;
			if (pDown >= pFlat && pDown >= pUp) return 0;
			return 1;
			}
		}
	}
