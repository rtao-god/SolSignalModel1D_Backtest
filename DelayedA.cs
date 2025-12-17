using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private enum DelayedResultCode
			{
			None = 0,
			TpFirst = 1,
			SlFirst = 2,
			Ambiguous = 3
			}

		private static void PopulateDelayedA (
			IList<BacktestRecord> records,
			List<BacktestRecord> allRows,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle6h> solAll6h,
			IReadOnlyList<Candle1m> sol1m,
			double dipFrac = 0.005,
			double tpPct = 0.010,
			double slPct = 0.010 )
			{
			if (records == null)
				throw new ArgumentNullException (nameof (records), "[PopulateDelayedA] records is null.");

			if (records.Count == 0)
				return;

			if (allRows == null || allRows.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] allRows is null or empty – cannot build pullback dataset.");

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] sol1m is null or empty – 1m candles are required for delayed A.");

			if (sol1h == null || sol1h.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] sol1h is null or empty – 1h candles are required for delayed A.");

			if (solAll6h == null || solAll6h.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] solAll6h is null or empty – expected non-empty 6h series.");

			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			List<PullbackContinuationSample> pullbackSamples = PullbackContinuationOfflineBuilder.Build (
				rows: allRows,
				sol1h: sol1h,
				sol6hDict: sol6hDict
			);

			Console.WriteLine ($"[PopulateDelayedA] built pullbackSamples = {pullbackSamples.Count}");

			if (pullbackSamples.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] No samples for model A – check input rows and candles consistency.");

			var pullbackTrainer = new PullbackContinuationTrainer ();
			DateTime asOfDate = allRows.Max (r => r.ToCausalDateUtc ()).AddDays (1);
			var pullbackModel = pullbackTrainer.Train (pullbackSamples, asOfDate);
			var pullbackEngine = pullbackTrainer.CreateEngine (pullbackModel);

			foreach (var rec in records)
				{
				if (rec == null)
					throw new InvalidOperationException ("[PopulateDelayedA] records contains null item.");

				if (rec.Causal == null)
					{
					throw new InvalidOperationException (
						$"[PopulateDelayedA] BacktestRecord.Causal is null for date {rec.DateUtc:O}.");
					}

				var causal = rec.Causal;

				// Важно: PopulateDelayedA может вызываться повторно (preview/what-if).
				// Любые "липкие" поля должны быть сброшены, иначе метрики/принтеры будут считать фантомы.
				ResetDelayedAState (rec, causal);

				bool wantLong = causal.PredLabel == 2 || (causal.PredLabel == 1 && causal.PredMicroUp);
				bool wantShort = causal.PredLabel == 0 || (causal.PredLabel == 1 && causal.PredMicroDown);

				// Инвариант: направление должно быть ровно одно либо ни одного (нет сигнала).
				if (wantLong == wantShort)
					{
					if (!wantLong)
						{
						// нет сигнала
						causal.DelayedWhyNot = "no signal";
						continue;
						}

					// оба true = неконсистентный прогноз, это pipeline-bug.
					throw new InvalidOperationException (
						$"[PopulateDelayedA] Ambiguous direction: wantLong && wantShort for {rec.DateUtc:O}. " +
						$"PredLabel={causal.PredLabel}, PredMicroUp={causal.PredMicroUp}, PredMicroDown={causal.PredMicroDown}");
					}

				// Гейт по SL-модели: A включается только если SL пометил день как рискованный.
				if (causal.SlHighDecision != true)
					{
					causal.DelayedWhyNot = "sl gate";
					continue;
					}

				DateTime dayStart = rec.DateUtc;

				DateTime dayEnd;
				try
					{
					dayEnd = Windowing.ComputeBaselineExitUtc (dayStart, Windowing.NyTz);
					}
				catch (Exception ex)
					{
					throw new InvalidOperationException (
						$"[PopulateDelayedA] Failed to compute baseline exit for dayStart={dayStart:O}.",
						ex);
					}

				bool strongSignal = (causal.PredLabel == 2 || causal.PredLabel == 0);

				if (double.IsNaN (causal.MinMove) || double.IsInfinity (causal.MinMove) || causal.MinMove <= 0.0)
					throw new InvalidOperationException ($"[PopulateDelayedA] MinMove must be finite and positive for {dayStart:O}.");

				double dayMinMove = causal.MinMove;

				// Санити: в baseline-окне должны быть 1h бары, иначе вход/окно данных сломаны.
				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= dayStart && h.OpenTimeUtc < dayEnd)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();

				if (dayHours.Count == 0)
					throw new InvalidOperationException ($"[PopulateDelayedA] No 1h candles in baseline window for {dayStart:O}.");

				// Важно: фичи должны строиться так же, как в offline builder — на полном ряду,
				// чтобы не было train/predict mismatch и случайного look-ahead через "обрезанный" список.
				var features = TargetLevelFeatureBuilder.Build (
					dayStart,
					wantLong,
					strongSignal,
					dayMinMove,
					rec.Entry,
					sol1h
				);

				var pullbackSample = new PullbackContinuationSample
					{
					Features = features,
					Label = false,
					EntryUtc = dayStart
					};

				var predA = pullbackEngine.Predict (pullbackSample);

				if (!predA.PredictedLabel || predA.Probability < 0.70f)
					{
					causal.DelayedWhyNot = "model gate";
					continue;
					}

				// A сказала "да"
				causal.DelayedSource = "A";
				causal.DelayedEntryAsked = true;
				causal.DelayedEntryUsed = true;

				var dayMinutes = sol1m
					.Where (m => m.OpenTimeUtc >= dayStart && m.OpenTimeUtc < dayEnd)
					.OrderBy (m => m.OpenTimeUtc)
					.ToList ();

				if (dayMinutes.Count == 0)
					throw new InvalidOperationException ($"[PopulateDelayedA] No 1m candles in baseline window for {dayStart:O}.");

				double triggerPrice = wantLong
					? rec.Entry * (1.0 - dipFrac)
					: rec.Entry * (1.0 + dipFrac);

				DateTime maxDelayTime = dayStart.AddHours (4);
				Candle1m? fillBar = null;

				foreach (var m in dayMinutes)
					{
					if (m.OpenTimeUtc > maxDelayTime)
						break;

					if (wantLong && m.Low <= triggerPrice) { fillBar = m; break; }
					if (wantShort && m.High >= triggerPrice) { fillBar = m; break; }
					}

				if (fillBar == null)
					{
					rec.DelayedEntryExecuted = false;
					rec.DelayedWhyNot = "no trigger";
					continue;
					}

				rec.DelayedEntryExecuted = true;
				rec.DelayedEntryExecutedAtUtc = fillBar.OpenTimeUtc;
				rec.DelayedEntryPrice = triggerPrice;

				double effectiveTpPct = tpPct;
				double effectiveSlPct = slPct;

				if (causal.MinMove > 0.0)
					{
					double linkedTp = causal.MinMove * 1.2;
					if (linkedTp > effectiveTpPct)
						effectiveTpPct = linkedTp;
					}

				// Эти поля позже читает PnL/принтеры (через BacktestRecord прокси или напрямую).
				causal.DelayedIntradayTpPct = effectiveTpPct;
				causal.DelayedIntradaySlPct = effectiveSlPct;

				double tpLevel = wantLong
					? rec.DelayedEntryPrice * (1.0 + effectiveTpPct)
					: rec.DelayedEntryPrice * (1.0 - effectiveTpPct);

				double slLevel = wantLong
					? rec.DelayedEntryPrice * (1.0 - effectiveSlPct)
					: rec.DelayedEntryPrice * (1.0 + effectiveSlPct);

				rec.DelayedIntradayResult = (int) DelayedIntradayResult.None;

				foreach (var m in dayMinutes)
					{
					if (m.OpenTimeUtc < rec.DelayedEntryExecutedAtUtc)
						continue;

					bool hitTp = wantLong ? (m.High >= tpLevel) : (m.Low <= tpLevel);
					bool hitSl = wantLong ? (m.Low <= slLevel) : (m.High >= slLevel);

					if (hitTp && hitSl) { rec.DelayedIntradayResult = (int) DelayedIntradayResult.Ambiguous; break; }
					if (hitTp) { rec.DelayedIntradayResult = (int) DelayedIntradayResult.TpFirst; break; }
					if (hitSl) { rec.DelayedIntradayResult = (int) DelayedIntradayResult.SlFirst; break; }
					}
				}

			static void ResetDelayedAState ( BacktestRecord rec, CausalPredictionRecord causal )
				{
				// Решение/гейт (каузально)
				causal.DelayedSource = null;
				causal.DelayedEntryAsked = false;
				causal.DelayedEntryUsed = false;
				causal.DelayedWhyNot = null;

				// Факты исполнения (omniscient)
				rec.DelayedEntryExecuted = false;
				rec.DelayedEntryExecutedAtUtc = null;
				rec.DelayedEntryPrice = 0.0;
				rec.DelayedWhyNot = null;
				rec.DelayedIntradayResult = (int) DelayedIntradayResult.None;

				causal.DelayedIntradayTpPct = null;
				causal.DelayedIntradaySlPct = null;
				}
			}
		}
	}
