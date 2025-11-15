using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Builders;
using SolSignalModel1D_Backtest.Core.ML.Delayed.States;
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
		private const float PullbackProbThresh = 0.70f;
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
				DelayedEntryExecutedAtUtc = null,
				SlHighDecision = false
				};

			if (!hasDir)
				return rec;

			// 1h и 1m за день
			var day1h = sol1h
				.Where (h => h.OpenTimeUtc >= dayRow.Date && h.OpenTimeUtc < dayRow.Date.AddHours (24))
				.OrderBy (h => h.OpenTimeUtc)
				.ToList ();

			var day1m = sol1m
				.Where (m => m.OpenTimeUtc >= dayRow.Date && m.OpenTimeUtc < dayRow.Date.AddHours (24))
				.OrderBy (m => m.OpenTimeUtc)
				.ToList ();

			bool strong = predCls == 2 || predCls == 0;
			double dayMinMove = dayRow.MinMove > 0 ? dayRow.MinMove : 0.02;

			// базовый исход — ТОЛЬКО по минуте
			var baseOutcome = MinuteTradeEvaluator.Evaluate (
				day1m,
				dayRow.Date,
				goLong,
				goShort,
				entry,
				dayMinMove,
				strong
			);

			// SL-фичи всё ещё по 1h
			if (slState.Engine != null && sol1h.Count > 0)
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

				double slProb = slPred.Probability;
				stats.AddSlScore (slProb, slPred.PredictedLabel, baseOutcome);

				bool slSaidRisk = slProb >= slState.SLRiskThreshold;
				rec.SlHighDecision = slSaidRisk;

				if (slSaidRisk)
					{
					// A
					bool wantA = false;
					if (pullbackState.Engine != null)
						{
						var featsA = TargetLevelFeatureBuilder.Build (
							dayRow.Date,
							goLong,
							strong,
							dayMinMove,
							entry,
							day1h
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
							0.45,
							4.0
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
							Result = delayed.Result,
							TpPct = delayed.TpPct,
							SlPct = delayed.SlPct
							});
						return rec;
						}

					// B (если включим)
					if (EnableDelayedB && smallState.Engine != null)
						{
						var featsB = TargetLevelFeatureBuilder.Build (
							dayRow.Date,
							goLong,
							strong,
							dayMinMove,
							entry,
							day1h
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
								0.18,
								2.0
							);

							rec.DelayedEntryUsed = true;
							rec.DelayedSource = "B";
							rec.DelayedEntryExecuted = delayed.Executed;
							rec.DelayedEntryPrice = delayed.TargetEntryPrice;
							rec.DelayedIntradayResult = (int) delayed.Result;
							rec.DelayedIntradayTpPct = delayed.TpPct;
							rec.DelayedIntradaySlPct = delayed.SlPct;
							rec.DelayedEntryExecutedAtUtc = delayed.ExecutedAtUtc;

							stats.AddDelayed ("B", new DelayedEntryResult
								{
								Executed = delayed.Executed,
								ExecutedAtUtc = delayed.ExecutedAtUtc,
								TargetEntryPrice = delayed.TargetEntryPrice,
								Result = delayed.Result,
								TpPct = delayed.TpPct,
								SlPct = delayed.SlPct
								});
							return rec;
							}
						}

					// риск был, но ничего не подошло
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

			// обычный день
			stats.AddImmediate (baseOutcome);
			return rec;
			}
		}
	}
