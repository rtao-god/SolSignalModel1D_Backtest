using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest
	{
	internal partial class Program
		{
		/// <summary>
		/// Тренируем SL-модель на каузальном оффлайн-датасете (SlOfflineBuilder)
		/// и проставляем SlProb / SlHighDecision в PredictionRecord.
		/// Любые проблемы с данными считаем фатальными и пробрасываем исключения.
		/// </summary>
		private static void TrainAndApplySlModelOffline (
			List<DataRow> allRows,
			IList<PredictionRecord> records,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle1m> sol1m,
			IReadOnlyList<Candle6h> solAll6h )
			{
			if (allRows == null || allRows.Count == 0)
				throw new InvalidOperationException ("[sl-offline] allRows is null or empty – SL-model cannot be trained.");

			if (records == null)
				throw new ArgumentNullException (nameof (records), "[sl-offline] records is null.");

			if (solAll6h == null || solAll6h.Count == 0)
				throw new InvalidOperationException ("[sl-offline] solAll6h is null or empty – expected non-empty 6h series.");

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1m is null or empty – 1m candles are required for SL-model.");

			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			var sol1hOrNull = sol1h != null && sol1h.Count > 0 ? sol1h : null;
			if (sol1hOrNull == null)
				throw new InvalidOperationException ("[sl-offline] sol1h is null or empty – 1h candles are required for SL-model.");

			// Строим SL-датасет: для каждого утреннего дня — гипотетический long/short, кто был первым: SL или TP
			var slSamples = SlOfflineBuilder.Build (
				rows: allRows,
				sol1h: sol1hOrNull,
				sol1m: sol1m,
				sol6hDict: sol6hDict
			);

			Console.WriteLine ($"[sl-offline] built samples = {slSamples.Count}");
			if (slSamples.Count < 20)
				throw new InvalidOperationException ($"[sl-offline] too few samples for SL-model: {slSamples.Count} < 20.");

			// Оффлайн-тренировка (без онлайн-доучивания)
			var trainer = new SlFirstTrainer ();
			var asOf = allRows.Max (r => r.Date);
			var slModel = trainer.Train (slSamples, asOf);
			var slEngine = trainer.CreateEngine (slModel);

			// Порог "HIGH" риска (positive класс = SL-first)
			const float SlRiskThreshold = 0.55f;

			// Быстрая мапа DataRow по дате
			var rowByDate = allRows.ToDictionary (r => r.Date, r => r);

			int scored = 0;

			foreach (var rec in records)
				{
				if (!rowByDate.TryGetValue (rec.DateUtc, out var row))
					throw new InvalidOperationException ($"[sl-runtime] No DataRow found for prediction date {rec.DateUtc:O}.");

				// Направление по дневной модели
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort)
					continue; // это не ошибка данных, а отсутствие торгового сигнала

				bool strong = rec.PredLabel == 2 || rec.PredLabel == 0;
				double dayMinMove = rec.MinMove > 0 ? rec.MinMove : 0.02;
				double entryPrice = rec.Entry;
				if (entryPrice <= 0)
					throw new InvalidOperationException ($"[sl-runtime] Non-positive entry price {entryPrice} for date {rec.DateUtc:O}.");

				// Фичи для SL-модели (6h-контекст через 2h-блоки + хвост 1h)
				var slFeats = SlFeatureBuilder.Build (
					entryUtc: rec.DateUtc,
					goLong: goLong,
					strongSignal: strong,
					dayMinMove: dayMinMove,
					entryPrice: entryPrice,
					candles1h: sol1hOrNull
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
				}

			Console.WriteLine ($"[sl-runtime] scored days = {scored}/{records.Count}");
			}
		}
	}
