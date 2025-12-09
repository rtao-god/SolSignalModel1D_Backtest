using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Diagnostics.SL;
using SolSignalModel1D_Backtest.Core.ML.SL;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Тренируем SL-модель на каузальном оффлайн-датасете (SlOfflineBuilder)
		/// и проставляем SlProb / SlHighDecision / Prob*_Total в PredictionRecord.
		/// Любые проблемы с данными считаем фатальными и пробрасываем исключения.
		/// Дополнительно в одном прогоне считаем train-метрики SL-модели
		/// для нескольких порогов MinMove (0.025/0.030/0.035), чтобы видеть,
		/// как выбор порога "сильного" дня влияет на качество.
		/// </summary>
		private static void TrainAndApplySlModelOffline (
			List<DataRow> allRows,
			IList<PredictionRecord> records,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle1m> sol1m,
			IReadOnlyList<Candle6h> solAll6h )
			{
			// ===== базовые проверки входа =====
			if (allRows == null || allRows.Count == 0)
				throw new InvalidOperationException ("[sl-offline] allRows is null or empty – SL-model cannot be trained.");

			if (records == null)
				throw new ArgumentNullException (nameof (records), "[sl-offline] records is null.");

			if (solAll6h == null || solAll6h.Count == 0)
				throw new InvalidOperationException ("[sl-offline] solAll6h is null or empty – expected non-empty 6h series.");

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1m is null or empty – 1m candles are required for SL-model.");

			if (sol1h == null || sol1h.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1h is null or empty – 1h candles are required for SL-model.");

			if (_trainUntilUtc == default)
				{
				throw new InvalidOperationException (
					"[sl-offline] _trainUntilUtc is not initialized. " +
					"CreatePredictionEngineOrFallback must be called before TrainAndApplySlModelOffline.");
				}

			// Кандидаты порогов MinMove для разделения strong/weak-сигналов.
			// Все значения в долях (0.03 = 3%).
			double[] strongMinMoveThresholds = { 0.025, 0.030, 0.035 };

			// Основной порог, который будет использоваться в рантайме (rec.SlProb / SlHighDecision).
			const double MainStrongMinMoveThreshold = 0.030;

			// ===== 1. Каузальный train-сабсет для SL-модели =====
			// Берём только те DataRow, которые лежат в train-периоде дневной модели.
			// Это убирает утечку: OOS-даты не попадают в обучающий датасет SL.
			var slTrainRows = allRows
				.Where (r => r.Date <= _trainUntilUtc)
				.OrderBy (r => r.Date)
				.ToList ();

			if (slTrainRows.Count < 50)
				{
				// Если train-сабсет слишком маленький — логируем предупреждение
				// и осознанно используем всю историю (train==test для SL).
				Console.WriteLine (
					$"[sl-offline] WARNING: slTrainRows too small ({slTrainRows.Count}), " +
					"training SL-model on full allRows (train==test for SL).");

				slTrainRows = allRows
					.OrderBy (r => r.Date)
					.ToList ();
				}

			// Логируем период train-части для SL.
			var slTrainMin = slTrainRows.Min (r => r.Date);
			var slTrainMax = slTrainRows.Max (r => r.Date);
			Console.WriteLine (
				$"[sl-offline] train rows = {slTrainRows.Count}, " +
				$"period = {slTrainMin:yyyy-MM-dd}..{slTrainMax:yyyy-MM-dd}, " +
				$"trainUntilUtc = {_trainUntilUtc:yyyy-MM-dd}");

			// Словарь 6h для оффлайн-лейблов (кто первый: SL/TP)
			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			// ===== 2. Построение SL-датасетов для разных порогов MinMove =====
			var samplesByThreshold = new Dictionary<double, List<SlHitSample>> ();

			foreach (var thr in strongMinMoveThresholds)
				{
				// Селектор "сильный/слабый" для данного порога:
				//   - мягкий пол 0.02;
				//   - логика strong/weak в SlStrongUtils.
				Func<DataRow, bool> strongSelector = r =>
				{
					double mm = r.MinMove > 0 ? r.MinMove : 0.02;
					return SlStrongUtils.IsStrongByMinMove (mm, r.RegimeDown, thr);
				};

				var samples = SlOfflineBuilder.Build (
					rows: slTrainRows,
					sol1h: sol1h,
					sol1m: sol1m,
					sol6hDict: sol6hDict,
					tpPct: 0.03,
					slPct: 0.05,
					strongSelector: strongSelector
				);

				samplesByThreshold[thr] = samples;

				int slCountThr = samples.Count (s => s.Label);
				int tpCountThr = samples.Count - slCountThr;

				Console.WriteLine (
					$"[sl-offline] thr={thr:0.000}: built samples = {samples.Count} (SL={slCountThr}, TP={tpCountThr})");
				}

			if (!samplesByThreshold.TryGetValue (MainStrongMinMoveThreshold, out var slSamples))
				{
				throw new InvalidOperationException (
					$"[sl-offline] internal error: no samples for main threshold {MainStrongMinMoveThreshold:0.000}");
				}

			if (slSamples.Count < 20)
				throw new InvalidOperationException ($"[sl-offline] too few samples for SL-model: {slSamples.Count} < 20.");

			// ===== 3. Оффлайн-тренировка основной SL-модели (для runtime) =====
			var trainer = new SlFirstTrainer ();
			var asOf = slTrainRows.Max (r => r.Date);
			var slModel = trainer.Train (slSamples, asOf);
			var slEngine = trainer.CreateEngine (slModel);

			// ===== 3a. Диагностика SL-модели (PFI + direction) на train-сабсете =====
			SlModelDiagnostics.LogFeatureImportanceOnSlModel (
				samples: slSamples,
				datasetTag: $"sl-train thr={MainStrongMinMoveThreshold:0.000}",
				modelOverride: slModel,
				featureNames: null);

			// Порог "HIGH" риска (positive класс = SL-first)
			const float SlRiskThreshold = 0.55f;

			// ===== 3.1. Санити-чек: train-метрики SL-модели для разных порогов MinMove =====
			foreach (var kv in samplesByThreshold.OrderBy (kv => kv.Key))
				{
				double thr = kv.Key;
				var samples = kv.Value;

				if (samples.Count == 0)
					{
					Console.WriteLine ($"[sl-train-debug:thr={thr:0.000}] no samples, skip.");
					continue;
					}

				// Для основного порога используем уже обученную модель,
				// для остальных — обучаем временные модели на том же asOf.
				PredictionEngine<SlHitSample, SlHitPrediction> engineForThisThr;

				if (Math.Abs (thr - MainStrongMinMoveThreshold) < 1e-9)
					{
					engineForThisThr = slEngine;
					}
				else
					{
					var tmpModel = trainer.Train (samples, asOf);
					engineForThisThr = trainer.CreateEngine (tmpModel);
					}

				DebugSlTrainMetrics (
					samples,
					engineForThisThr,
					SlRiskThreshold,
					tag: $"thr={thr:0.000}");
				}

			// ===== 4. Runtime-применение SL к PredictionRecord =====

			int scored = 0;
			int predHighDays = 0;
			int overlayApplied = 0;

			double minProb = double.PositiveInfinity;
			double maxProb = double.NegativeInfinity;
			double sumProb = 0.0;
			int probCount = 0;

			foreach (var rec in records)
				{
				// Направление по дневной модели (с учётом микро-слоя).
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);

				if (!goLong && !goShort)
					{
					// Нет торгового сигнала – SL не считаем и Prob*_Total не трогаем.
					continue;
					}

				// Сильный/слабый сигнал определяется по MinMove,
				// и используется и в оффлайн-датасете, и в runtime.
				double dayMinMove = rec.MinMove > 0 ? rec.MinMove : 0.02;
				bool strong = SlStrongUtils.IsStrongByMinMove (dayMinMove, rec.RegimeDown, MainStrongMinMoveThreshold);

				double entryPrice = rec.Entry;
				if (entryPrice <= 0)
					{
					throw new InvalidOperationException (
						$"[sl-runtime] Non-positive entry price {entryPrice} for date {rec.DateUtc:O}.");
					}

				// Фичи для SL-модели строятся из 1h-контекста вокруг entryUtc.
				var slFeats = SlFeatureBuilder.Build (
					entryUtc: rec.DateUtc,
					goLong: goLong,
					strongSignal: strong,
					dayMinMove: dayMinMove,
					entryPrice: entryPrice,
					candles1h: sol1h
				);

				var slPred = slEngine.Predict (new SlHitSample
					{
					Label = false,          // в рантайме не используется
					Features = slFeats,
					EntryUtc = rec.DateUtc
					});

				double p = slPred.Probability;
				bool predHigh = slPred.PredictedLabel && p >= SlRiskThreshold;

				rec.SlProb = p;
				rec.SlHighDecision = predHigh;
				scored++;

				// Применяем SL-оверлей к Day+Micro-вероятностям → Total.
				SlOverlayApplier.Apply (
					rec,
					slProb: p,
					goLong: goLong,
					goShort: goShort,
					strongSignal: strong);

				overlayApplied++;

				// Агрегация статистики по вероятностям.
				sumProb += p;
				probCount++;
				if (p < minProb) minProb = p;
				if (p > maxProb) maxProb = p;
				if (predHigh) predHighDays++;
				}

			// Логируем период records, чтобы понимать, какие даты реально проверяются.
			if (records.Count > 0)
				{
				var recMin = records.Min (r => r.DateUtc);
				var recMax = records.Max (r => r.DateUtc);
				Console.WriteLine (
					$"[sl-runtime] records period = {recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}, " +
					$"count = {records.Count}");
				}

			if (probCount > 0)
				{
				double avgProb = sumProb / probCount;
				Console.WriteLine (
					$"[sl-runtime] scored days = {scored}/{records.Count}, " +
					$"overlayApplied={overlayApplied}, " +
					$"predHigh={predHighDays}, " +
					$"prob range = [{minProb:0.000}..{maxProb:0.000}], avg={avgProb:0.000}, " +
					$"thr={SlRiskThreshold:0.00}, strongMinMove={MainStrongMinMoveThreshold:P1}");
				}
			else
				{
				Console.WriteLine (
					$"[sl-runtime] scored days = {scored}/{records.Count}, " +
					"no SL-scores produced (no trading days with direction).");
				}
			}

		/// <summary>
		/// Подробный лог train-метрик SL-модели для заданного порога MinMove.
		/// Используется только для диагностики, не влияет на runtime.
		/// </summary>
		private static void DebugSlTrainMetrics (
			List<SlHitSample> samples,
			PredictionEngine<SlHitSample, SlHitPrediction> engine,
			float riskThreshold,
			string tag )
			{
			int trainPos = 0;      // реальных SL-дней в оффлайн-семплах
			int trainNeg = 0;      // TP-дней
			int trainPredHigh = 0; // сколько раз модель сказала "HIGH"
			int trainTp = 0;       // HIGH & реально SL
			int trainFp = 0;       // HIGH & реально TP

			foreach (var s in samples)
				{
				if (s.Label) trainPos++;
				else trainNeg++;

				var pred = engine.Predict (s);
				double p = pred.Probability;
				bool high = pred.PredictedLabel && p >= riskThreshold;
				if (!high) continue;

				trainPredHigh++;
				if (s.Label) trainTp++;
				else trainFp++;
				}

			double tprTrain = trainPos > 0 ? (double) trainTp / trainPos : 0.0;
			double fprTrain = trainNeg > 0 ? (double) trainFp / trainNeg : 0.0;

			Console.WriteLine (
				$"[sl-train-debug:{tag}] pos={trainPos}, neg={trainNeg}, predHigh={trainPredHigh}, " +
				$"TPR={tprTrain:P1}, FPR={fprTrain:P1}, thr={riskThreshold:0.00}");
			}
		}
	}
