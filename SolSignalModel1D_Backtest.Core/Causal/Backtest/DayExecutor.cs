using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Core.Causal.Backtest
	{
	/// <summary>
	/// Исполнитель дневной каузальной модели:
	/// на входе DataRow + PredictionEngine, на выходе CausalPredictionRecord.
	/// Никакого доступа к forward-окну и 1m-пути здесь нет.
	/// </summary>
	public static class DayExecutor
		{
		private static int ArgmaxLabel ( double pUp, double pFlat, double pDown )
			{
			if (pUp >= pFlat && pUp >= pDown) return 2;
			if (pDown >= pFlat && pDown >= pUp) return 0;
			return 1;
			}

		public static CausalPredictionRecord ProcessDay (
			DataRow dayRow,
			PredictionEngine dailyEngine )
			{
			if (dayRow == null) throw new ArgumentNullException (nameof (dayRow));
			if (dailyEngine == null) throw new ArgumentNullException (nameof (dailyEngine));

			var pred = dailyEngine.Predict (dayRow);
			int predCls = pred.Class;

			if (predCls != 0 && predCls != 1 && predCls != 2)
				{
				throw new InvalidOperationException (
					$"[DayExecutor] Unexpected prediction class '{predCls}' for date {dayRow.Date:O}. Expected 0, 1 or 2.");
				}

			var dayProbs = pred.Day;
			var dayMicroProbs = pred.DayWithMicro;
			var micro = pred.Micro;

			double daySum = dayProbs.PUp + dayProbs.PFlat + dayProbs.PDown;
			double dayMicroSum = dayMicroProbs.PUp + dayMicroProbs.PFlat + dayMicroProbs.PDown;

			if (daySum <= 0.0 || dayMicroSum <= 0.0)
				{
				throw new InvalidOperationException (
					$"[DayExecutor] Invalid probabilities from PredictionEngine for {dayRow.Date:O}. " +
					$"daySum={daySum}, dayMicroSum={dayMicroSum}, " +
					$"P_day=({dayProbs.PUp}, {dayProbs.PFlat}, {dayProbs.PDown}), " +
					$"P_dayMicro=({dayMicroProbs.PUp}, {dayMicroProbs.PFlat}, {dayMicroProbs.PDown}).");
				}

			int predLabelDay = ArgmaxLabel (dayProbs.PUp, dayProbs.PFlat, dayProbs.PDown);
			int predLabelDayMicro = ArgmaxLabel (dayMicroProbs.PUp, dayMicroProbs.PFlat, dayMicroProbs.PDown);

			// На момент дневной модели считаем, что Total = Day+Micro.
			int predLabelTotal = predLabelDayMicro;

			bool microPredicted = micro.ConsiderUp || micro.ConsiderDown;

			var causal = new CausalPredictionRecord
				{
				// базовая идентичность дня
				DateUtc = dayRow.Date,
				TrueLabel = dayRow.Label,

				// классы
				PredLabel = predCls,
				PredLabel_Day = predLabelDay,
				PredLabel_DayMicro = predLabelDayMicro,
				PredLabel_Total = predLabelTotal,

				// Day
				ProbUp_Day = dayProbs.PUp,
				ProbFlat_Day = dayProbs.PFlat,
				ProbDown_Day = dayProbs.PDown,

				// Day+Micro
				ProbUp_DayMicro = dayMicroProbs.PUp,
				ProbFlat_DayMicro = dayMicroProbs.PFlat,
				ProbDown_DayMicro = dayMicroProbs.PDown,

				// Total (на этом этапе = Day+Micro, SL/Delayed модифицируют позже).
				ProbUp_Total = dayMicroProbs.PUp,
				ProbFlat_Total = dayMicroProbs.PFlat,
				ProbDown_Total = dayMicroProbs.PDown,

				Conf_Day = dayProbs.Confidence,
				Conf_Micro = dayMicroProbs.Confidence,

				// микро-слой
				MicroPredicted = microPredicted,
				PredMicroUp = micro.ConsiderUp,
				PredMicroDown = micro.ConsiderDown,
				FactMicroUp = dayRow.FactMicroUp,
				FactMicroDown = dayRow.FactMicroDown,

				// контекст/режим
				RegimeDown = dayRow.RegimeDown,
				Reason = pred.Reason ?? string.Empty,
				MinMove = dayRow.MinMove,

				// SL-модель (заполняется позже оффлайном)
				SlProb = 0.0,
				SlHighDecision = false,
				Conf_SlLong = 0.0,
				Conf_SlShort = 0.0,

				// delayed-слой (A/B; заполняется PopulateDelayedA/B)
				DelayedSource = null,
				DelayedEntryAsked = false,
				DelayedEntryUsed = false,
				DelayedIntradayTpPct = 0.0,
				DelayedIntradaySlPct = 0.0,
				TargetLevelClass = 0
				};

			return causal;
			}
		}
	}
