using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Builders;
using SolSignalModel1D_Backtest.Core.ML.Delayed.States;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public static class DayExecutor
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		private const float PullbackProbThresh = 0.85f;
		private const float SmallProbThresh = 0.75f;
		private const bool EnableDelayedB = false;

		private static int ArgmaxLabel ( double pUp, double pFlat, double pDown )
			{
			if (pUp >= pFlat && pUp >= pDown) return 2;
			if (pDown >= pFlat && pDown >= pUp) return 0;
			return 1;
			}

		public static BacktestRecord ProcessDay (
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
			// дневной предикт из PredictionEngine: класс + P_day + P_dayMicro
			var pred = dailyEngine.Predict (dayRow);
			int predCls = pred.Class;

			if (predCls != 0 && predCls != 1 && predCls != 2)
				{
				throw new InvalidOperationException (
					$"Unexpected prediction class '{predCls}' for date {dayRow.Date:o}. Expected 0, 1 or 2.");
				}

			var dayProbs = pred.Day;
			var dayMicroProbs = pred.DayWithMicro;

			// Sanity-check: дневные вероятности должны быть валидными распределениями.
			double daySum = dayProbs.PUp + dayProbs.PFlat + dayProbs.PDown;
			double dayMicroSum = dayMicroProbs.PUp + dayMicroProbs.PFlat + dayMicroProbs.PDown;

			if (daySum <= 0.0 || dayMicroSum <= 0.0)
				{
				throw new InvalidOperationException (
					$"[DayExecutor] Invalid probabilities from PredictionEngine for {dayRow.Date:o}. " +
					$"daySum={daySum}, dayMicroSum={dayMicroSum}, " +
					$"P_day=({dayProbs.PUp}, {dayProbs.PFlat}, {dayProbs.PDown}), " +
					$"P_dayMicro=({dayMicroProbs.PUp}, {dayMicroProbs.PFlat}, {dayMicroProbs.PDown}).");
				}

			int predLabelDay = ArgmaxLabel (dayProbs.PUp, dayProbs.PFlat, dayProbs.PDown);
			int predLabelDayMicro = ArgmaxLabel (dayMicroProbs.PUp, dayMicroProbs.PFlat, dayMicroProbs.PDown);

			// До SL-оверлея считаем, что Total = Day+Micro.
			var totalProbs = dayMicroProbs;
			int predLabelTotal = predLabelDayMicro;

			var micro = pred.Micro;
			string reason = pred.Reason;

			var fwd = BacktestHelpers.GetForwardInfo (dayRow.Date, sol6hDict);
			double entry = fwd.entry;

			bool goLong = predCls == 2;
			bool goShort = predCls == 0;
			bool hasDir = goLong || goShort;

			var rec = new BacktestRecord
				{
				DateUtc = dayRow.Date,

				// факт и исходный дневной класс (PredLabel остаётся "как было")
				TrueLabel = dayRow.Label,
				PredLabel = predCls,

				// Day-слой
				PredLabel_Day = predLabelDay,
				ProbUp_Day = dayProbs.PUp,
				ProbFlat_Day = dayProbs.PFlat,
				ProbDown_Day = dayProbs.PDown,
				Conf_Day = dayProbs.Confidence,

				// Day+Micro
				PredLabel_DayMicro = predLabelDayMicro,
				ProbUp_DayMicro = dayMicroProbs.PUp,
				ProbFlat_DayMicro = dayMicroProbs.PFlat,
				ProbDown_DayMicro = dayMicroProbs.PDown,
				Conf_Micro = dayMicroProbs.Confidence,

				// Total (по умолчанию = Day+Micro, будет обновлено после SL-оверлея)
				PredLabel_Total = predLabelTotal,
				ProbUp_Total = totalProbs.PUp,
				ProbFlat_Total = totalProbs.PFlat,
				ProbDown_Total = totalProbs.PDown,

				// микро-факты/прогнозы
				PredMicroUp = micro.ConsiderUp,
				PredMicroDown = micro.ConsiderDown,
				FactMicroUp = dayRow.FactMicroUp,
				FactMicroDown = dayRow.FactMicroDown,

				// цены дня
				Entry = entry,
				MaxHigh24 = fwd.maxHigh,
				MinLow24 = fwd.minLow,
				Close24 = fwd.fwdClose,

				// контекст
				RegimeDown = dayRow.RegimeDown,
				Reason = reason,
				MinMove = dayRow.MinMove,

				// delayed A/B
				DelayedSource = string.Empty,
				DelayedEntryAsked = false,
				DelayedEntryUsed = false,
				DelayedEntryExecuted = false,
				DelayedEntryPrice = 0.0,
				DelayedIntradayResult = 0,
				DelayedIntradayTpPct = 0.0,
				DelayedIntradaySlPct = 0.0,
				DelayedEntryExecutedAtUtc = null,

				// SL online
				SlProb = 0.0,
				SlHighDecision = false,
				Conf_SlLong = 0.0,
				Conf_SlShort = 0.0,

				// Anti-D
				AntiDirectionApplied = false
				};

			// Для дней без направления SL-оверлей не считается, Total уже = Day+Micro.
			if (!hasDir)
				return rec;

			// 1h и 1m для фактического исхода дня
			var entryUtc = dayRow.Date;
			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz);

			var day1h = sol1h
				.Where (h => h.OpenTimeUtc >= entryUtc && h.OpenTimeUtc < exitUtc)
				.ToList ();

			var day1m = sol1m
				.Where (m => m.OpenTimeUtc >= entryUtc && m.OpenTimeUtc < exitUtc)
				.ToList ();

			if (dayRow.MinMove <= 0)
				throw new InvalidOperationException ($"MinMove <= 0 for {dayRow.Date:o}. Raw value: {dayRow.MinMove}");

			double dayMinMove = dayRow.MinMove;

			bool strong = SlUtils.IsStrongByMinMove (dayMinMove);

			var baseOutcome = MinuteTradeEvaluator.Evaluate (
				day1m,
				dayRow.Date,
				goLong,
				goShort,
				entry,
				dayMinMove,
				strong,
				NyTz
			);

			// SL-оверлей + online-SL.
			if (slState.Engine != null && sol1h.Count > 0)
				{
				var slFeats = SlFeatureBuilder.Build (
					rec.DateUtc,
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

				double slProb = slPred.Probability;
				bool slPredictedSlFirst = slPred.PredictedLabel;

				rec.SlProb = slProb;

				stats.AddSlScore (slProb, slPredictedSlFirst, baseOutcome);

				// Строим SlProbabilities для оверлея.
				var slProbs = new SlProbabilities ();

				if (slPredictedSlFirst)
					{
					if (goLong)
						{
						slProbs.PSlLong = slProb;
						slProbs.ConfidenceLong = slProb;
						rec.Conf_SlLong = slProb;
						}

					if (goShort)
						{
						slProbs.PSlShort = slProb;
						slProbs.ConfidenceShort = slProb;
						rec.Conf_SlShort = slProb;
						}
					}

				// SL-оверлей: Day+Micro → Total.
				var aggCfg = new ProbabilityAggregationConfig ();
				totalProbs = ProbabilityAggregator.ApplySlOverlay (dayMicroProbs, slProbs, aggCfg);
				predLabelTotal = ArgmaxLabel (totalProbs.PUp, totalProbs.PFlat, totalProbs.PDown);

				rec.ProbUp_Total = totalProbs.PUp;
				rec.ProbFlat_Total = totalProbs.PFlat;
				rec.ProbDown_Total = totalProbs.PDown;
				rec.PredLabel_Total = predLabelTotal;

				bool slSaidRisk = slPredictedSlFirst && slProb >= slState.SLRiskThreshold;
				rec.SlHighDecision = slSaidRisk;

				if (slSaidRisk)
					{
					// ===== Model A (deep pullback) =====
					bool wantA = false;
					if (strong && pullbackState.Engine != null)
						{
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

						stats.AddDelayed ("A", new DelayedEntryResult
							{
							Executed = delayed.Executed,
							ExecutedAtUtc = delayed.ExecutedAtUtc,
							TargetEntryPrice = delayed.TargetEntryPrice,
							Result = (DelayedIntradayResult) (int) delayed.Result,
							TpPct = delayed.TpPct,
							SlPct = delayed.SlPct
							});
						return rec;
						}

					// ===== Model B =====
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
				// SL-модели нет → Total остаётся равным Day+Micro.
				stats.AddImmediate (baseOutcome);
				return rec;
				}

			// обычный день без вмешательства delayed-слоя (SL-оверлей уже учтён выше)
			stats.AddImmediate (baseOutcome);
			return rec;
			}
		}
	}
