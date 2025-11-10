using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public static class DayExecutor
		{
		// пороги для онлайн-прогонов A/B — можно крутить
		private const float PullbackProbThresh = 0.70f;
		private const float SmallProbThresh = 0.75f;

		public static PredictionRecord ProcessDay (
			DataRow dayRow,
			PredictionEngine dailyEngine,
			IReadOnlyList<Candle1h>? sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict,
			SlOnlineState slState,
			PullbackContinuationOnlineState pullbackState,
			SmallImprovementOnlineState smallState,
			Analytics.ExecutionStats stats )
			{
			// 1. дневной прогноз
			var (predCls, _, reason, micro) = dailyEngine.Predict (dayRow);

			var fwd = BacktestHelpers.GetForwardInfo (dayRow.Date, sol6hDict);
			double entry = fwd.entry;

			bool goLong = predCls == 2 || (predCls == 1 && micro.ConsiderUp);
			bool goShort = predCls == 0 || (predCls == 1 && micro.ConsiderDown);
			bool hasDir = goLong || goShort;

			var rec = new PredictionRecord
				{
				DateUtc = dayRow.Date,
				TrueLabel = dayRow.Label,
				PredLabel = predCls,
				PredMicroUp = micro.ConsiderUp,
				PredMicroDown = micro.ConsiderDown,
				FactMicroUp = dayRow.FactMicroUp,
				FactMicroDown = dayRow.FactMicroDown,
				Entry = entry,
				MaxHigh24 = fwd.maxHigh,
				MinLow24 = fwd.minLow,
				Close24 = fwd.fwdClose,
				RegimeDown = dayRow.RegimeDown,
				Reason = reason,
				MinMove = dayRow.MinMove,
				DelayedSource = string.Empty,
				DelayedEntryUsed = false,
				DelayedEntryExecuted = false,
				DelayedEntryPrice = 0.0,
				DelayedIntradayResult = 0,
				DelayedIntradayTpPct = 0.0,
				DelayedIntradaySlPct = 0.0,
				 SlHighDecision = false,
				DelayedEntryExecutedAtUtc = null
				};

			// если нет направления или нет 1h — просто вернуть дневной рекорд
			if (!hasDir || sol1h == null || sol1h.Count == 0)
				return rec;

			bool strong = predCls == 2 || predCls == 0;
			double dayMinMove = dayRow.MinMove;
			if (dayMinMove <= 0) dayMinMove = 0.02;

			// 2. базовый исход за 24h от входа в 12:00
			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				sol1h,
				dayRow.Date,
				goLong,
				goShort,
				entry,
				dayMinMove,
				strong
			);

			// 3. SL-оценка
			bool slSaidRisk = false;
			double slProb = 0.0;

			if (slState.Engine != null)
				{
				var slFeats = SlFeatureBuilder.Build (
					dayRow.Date,
					goLong,
					strong,
					dayMinMove,
					entry,
					sol1h
				);

				var slPred = slState.Engine.Predict (new SlHitSample
					{
					Label = false,
					Features = slFeats,
					EntryUtc = dayRow.Date
					});

				slProb = slPred.Probability;
				stats.AddSlScore (slProb, slPred.PredictedLabel, baseOutcome);

				slSaidRisk = slPred.Probability >= slState.SLRiskThreshold;

				rec.SlHighDecision = slSaidRisk;
				}

			// если день нормальный — просто фиксируем базу и выходим
			if (!slSaidRisk)
				{
				stats.AddImmediate (baseOutcome);
				return rec;
				}

			// соберём 24h бары на день, чтобы A/B иметь intraday-картинку
			var dayHours = sol1h
				.Where (h => h.OpenTimeUtc >= dayRow.Date && h.OpenTimeUtc < dayRow.Date.AddHours (24))
				.OrderBy (h => h.OpenTimeUtc)
				.ToList ();

			// 4. пробуем "глубокий откат" (A)
			bool wantA = false;
			if (pullbackState.Engine != null)
				{
				var featsA = TargetLevelFeatureBuilder.Build (
					dayRow.Date,
					goLong,
					strong,
					dayMinMove,
					entry,
					dayHours
				);

				var sampleA = new PullbackContinuationSample
					{
					Label = false,
					Features = featsA,
					EntryUtc = dayRow.Date
					};

				var predA = pullbackState.Engine.Predict (sampleA);
				if (predA.Probability >= PullbackProbThresh)
					wantA = true;
				}

			if (wantA)
				{
				var delayed = DelayedEntryEvaluator.Evaluate (
					sol1h,
					dayRow.Date,
					goLong,
					goShort,
					entry,
					dayMinMove,
					strong,
					0.45,   // deep
					4.0     // up to 4h
				);

				rec.DelayedEntryUsed = true;
				rec.DelayedSource = "A";
				rec.DelayedEntryExecuted = delayed.Executed;
				rec.DelayedEntryPrice = delayed.TargetEntryPrice;
				rec.DelayedIntradayResult = (int) delayed.Result;
				rec.DelayedIntradayTpPct = delayed.TpPct;
				rec.DelayedIntradaySlPct = delayed.SlPct;
				rec.DelayedEntryExecutedAtUtc = delayed.ExecutedAtUtc;

				stats.AddDelayed ("A", delayed);
				return rec;
				}

			// 5. пробуем "мелкий откат" (B)
			bool wantB = false;
			if (smallState.Engine != null)
				{
				var featsB = TargetLevelFeatureBuilder.Build (
					dayRow.Date,
					goLong,
					strong,
					dayMinMove,
					entry,
					dayHours
				);

				var sampleB = new SmallImprovementSample
					{
					Label = false,
					Features = featsB,
					EntryUtc = dayRow.Date
					};

				var predB = smallState.Engine.Predict (sampleB);
				if (predB.Probability >= SmallProbThresh)
					wantB = true;
				}

			if (wantB)
				{
				var delayed = DelayedEntryEvaluator.Evaluate (
					sol1h,
					dayRow.Date,
					goLong,
					goShort,
					entry,
					dayMinMove,
					strong,
					0.18,   // small
					2.0     // up to 2h
				);

				rec.DelayedEntryUsed = true;
				rec.DelayedSource = "B";
				rec.DelayedEntryExecuted = delayed.Executed;
				rec.DelayedEntryPrice = delayed.TargetEntryPrice;
				rec.DelayedIntradayResult = (int) delayed.Result;
				rec.DelayedIntradayTpPct = delayed.TpPct;
				rec.DelayedIntradaySlPct = delayed.SlPct;
				rec.DelayedEntryExecutedAtUtc = delayed.ExecutedAtUtc;

				stats.AddDelayed ("B", delayed);
				return rec;
				}

			// SL сказал "опасно", но A/B не нашли разумного отката → считаем как обычный "immediate"
			stats.AddImmediate (baseOutcome);
			return rec;
			}
		}
	}
