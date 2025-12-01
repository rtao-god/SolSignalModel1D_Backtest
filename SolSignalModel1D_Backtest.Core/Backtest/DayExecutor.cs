using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Builders;
using SolSignalModel1D_Backtest.Core.ML.Delayed.States;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public static class DayExecutor
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		private const float PullbackProbThresh = 0.85f;  // было 0.70f
		private const float SmallProbThresh = 0.75f;
		private const bool EnableDelayedB = false;

		public static PredictionRecord ProcessDay (
			DataRow dayRow,
			PredictionEngine dailyEngine,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle1m> sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict,
			SlOnlineState slState,
			PullbackContinuationOnlineState pullbackState,
			SmallImprovementOnlineState smallState,
			Analytics.ExecutionStats stats )
			{
			// дневной предикт из PredictionEngine
			var pred = dailyEngine.Predict (dayRow);
			int predCls = pred.Class;
			var micro = pred.Micro;
			string reason = pred.Reason;

			var fwd = BacktestHelpers.GetForwardInfo (dayRow.Date, sol6hDict);
			double entry = fwd.entry;
			// Вариант БЕЗ влияния микро-слоя на направление:
			// - торгуем только по основному классу (0 = down, 2 = up);
			// - класс 1 (flat) вообще не даёт сделки. потом добавить || (predCls == 1 && micro.ConsiderUp);   и || (predCls == 1 && micro.ConsiderDown);
			bool goLong = predCls == 2;
			bool goShort = predCls == 0;
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
				DelayedEntryExecutedAtUtc = null,
				SlHighDecision = false
				};

			if (!hasDir)
				return rec;

			// 1h и 1m только для "фактов" внутри дня
			var day1h = sol1h
				.Where (h => h.OpenTimeUtc >= dayRow.Date && h.OpenTimeUtc < dayRow.Date.AddHours (24))
				.ToList ();

			var day1m = sol1m
				.Where (m => m.OpenTimeUtc >= dayRow.Date && m.OpenTimeUtc < dayRow.Date.AddHours (24))
				.ToList ();

			double dayMinMove = dayRow.MinMove > 0 ? dayRow.MinMove : 0.02;
			// единая логика strong/weak для train и runtime
			bool strong = SlUtils.IsStrongByMinMove (dayMinMove /*, rec.RegimeDown */);

			// базовый исход — ТОЛЬКО по минуте (факт, для статистики)
			var baseOutcome = MinuteTradeEvaluator.Evaluate (
				day1m,
				dayRow.Date,
				goLong,
				goShort,
				entry,
				dayMinMove,
				strong
			);

			// SL-фичи по 1h
			if (slState.Engine != null && sol1h.Count > 0)
				{
				var slFeats = SlFeatureBuilder.Build (
					rec.DateUtc,     // дата входа
					goLong,          // направление
					strong,          // сильный/не сильный сигнал
					dayMinMove,      // MinMove дня
					entry,           // цена входа
					sol1h            // ВСЯ 1h-история SOL
				);

				var slPred = slState.Engine.Predict (new SlHitSample
					{
					// Label в рантайме не используется, но безопасно заполним false
					Label = false,
					Features = slFeats,
					EntryUtc = dayRow.Date
					});

				double slProb = slPred.Probability;
				bool slPredictedSlFirst = slPred.PredictedLabel;

				stats.AddSlScore (slProb, slPredictedSlFirst, baseOutcome);

				// HIGH = модель считает, что первым будет SL И уверенность выше порога
				bool slSaidRisk = slPredictedSlFirst && slProb >= slState.SLRiskThreshold;
				rec.SlHighDecision = slSaidRisk;

				if (slSaidRisk)
					{
					// ===== Model A (deep pullback) =====
					bool wantA = false;
					if (strong && pullbackState.Engine != null)
						{
						// фичи только из окна [t-6h, t) по всей SOL 1h истории
						var featsA = TargetLevelFeatureBuilder.Build (
							entryUtc: dayRow.Date,
							goLong: goLong,
							strongSignal: strong,
							dayMinMove: dayMinMove,
							entryPrice: entry,
							candles1h: sol1h
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
						var delayed = MinuteDelayedEntryEvaluator.Evaluate (
							day1m,
							dayRow.Date,
							goLong,
							goShort,
							entry,
							dayMinMove,
							strong,
							delayFactor: 0.45,
							maxDelayHours: 4.0,
							nyTz: NyTz
						);

						rec.DelayedEntryUsed = true;
						rec.DelayedSource = "A";
						rec.DelayedEntryExecuted = delayed.Executed;
						rec.DelayedEntryPrice = delayed.TargetEntryPrice;
						rec.DelayedIntradayResult = (int) delayed.Result;
						rec.DelayedIntradayTpPct = delayed.TpPct;
						rec.DelayedIntradaySlPct = delayed.SlPct;
						rec.DelayedEntryExecutedAtUtc = delayed.ExecutedAtUtc;

						stats.AddDelayed ("A", new Trading.Evaluator.DelayedEntryResult
							{
							Executed = delayed.Executed,
							ExecutedAtUtc = delayed.ExecutedAtUtc,
							TargetEntryPrice = delayed.TargetEntryPrice,
							Result = (Trading.Evaluator.DelayedIntradayResult) (int) delayed.Result,
							TpPct = delayed.TpPct,
							SlPct = delayed.SlPct
							});
						return rec;
						}

					// ===== Model B (мелкое улучшение, если включим) =====
					if (EnableDelayedB && smallState.Engine != null)
						{
						var featsB = TargetLevelFeatureBuilder.Build (
							entryUtc: dayRow.Date,
							goLong: goLong,
							strongSignal: strong,
							dayMinMove: dayMinMove,
							entryPrice: entry,
							candles1h: sol1h
						);

						var sampleB = new SmallImprovementSample
							{
							Label = false,
							Features = featsB,
							EntryUtc = dayRow.Date
							};

						var predB = smallState.Engine.Predict (sampleB);
						if (predB.Probability >= SmallProbThresh)
							{
							var delayed = MinuteDelayedEntryEvaluator.Evaluate (
								day1m,
								dayRow.Date,
								goLong,
								goShort,
								entry,
								dayMinMove,
								strong,
								delayFactor: 0.18,
								maxDelayHours: 2.0,
								nyTz: NyTz
							);

							rec.DelayedEntryUsed = true;
							rec.DelayedSource = "B";
							rec.DelayedEntryExecuted = delayed.Executed;
							rec.DelayedEntryPrice = delayed.TargetEntryPrice;
							rec.DelayedIntradayResult = (int) delayed.Result;
							rec.DelayedIntradayTpPct = delayed.TpPct;
							rec.DelayedIntradaySlPct = delayed.SlPct;
							rec.DelayedEntryExecutedAtUtc = delayed.ExecutedAtUtc;

							stats.AddDelayed ("B", new Trading.Evaluator.DelayedEntryResult
								{
								Executed = delayed.Executed,
								ExecutedAtUtc = delayed.ExecutedAtUtc,
								TargetEntryPrice = delayed.TargetEntryPrice,
								Result = (Trading.Evaluator.DelayedIntradayResult) (int) delayed.Result,
								TpPct = delayed.TpPct,
								SlPct = delayed.SlPct
								});
							return rec;
							}
						}

					// риск был, но ни A, ни B не сработали → остаёмся на "мгновенном" исходе
					stats.AddImmediate (baseOutcome);
					return rec;
					}
				}
			else
				{
				// SL-модели нет → просто фиксируем базовый вариант
				stats.AddImmediate (baseOutcome);
				return rec;
				}

			// обычный день без вмешательства delayed-слоя
			stats.AddImmediate (baseOutcome);
			return rec;
			}
		}
	}
