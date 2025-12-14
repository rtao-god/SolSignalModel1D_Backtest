using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		// Локальный enum для кодирования результата intraday-эксперимента A в int-поле PredictionRecord.
		// Значения подобраны под стандартный enum без явных присваиваний (0,1,2,3).
		private enum DelayedResultCode
			{
			None = 0,
			TpFirst = 1,
			SlFirst = 2,
			Ambiguous = 3
			}

		/// <summary>
		/// Модель A для отложенного входа.
		/// Любые проблемы с исходными рядами считаются фатальными – бросаем исключение.
		/// </summary>
		private static void PopulateDelayedA (
			IList<BacktestRecord> records,
			List<BacktestRecord> allRows,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle6h> solAll6h,
			IReadOnlyList<Candle1m> sol1m,
			double dipFrac = 0.005,   // 0.5% откат для входа
			double tpPct = 0.010,     // базовый 1.0% TP
			double slPct = 0.010 )    // базовый 1.0% SL
			{
			if (records == null)
				throw new ArgumentNullException (nameof (records), "[PopulateDelayedA] records is null.");

			// Пустой список без записей – это не ошибка данных, просто нечего обогащать delayed A.
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

			// Словарь 6h для оффлайн-датасета модели A
			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			// *** Оффлайн-датасет для модели A (deep pullback) ***
			List<PullbackContinuationSample> pullbackSamples = PullbackContinuationOfflineBuilder.Build (
				rows: allRows,
				sol1h: sol1h,
				sol6hDict: sol6hDict
			);

			Console.WriteLine ($"[PopulateDelayedA] built pullbackSamples = {pullbackSamples.Count}");

			if (pullbackSamples.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] No samples for model A – check input rows and candles consistency.");

			// Обучаем модель A на всей выборке (каузально: asOf = последняя дата + 1 день)
			var pullbackTrainer = new PullbackContinuationTrainer ();
			DateTime asOfDate = allRows.Max (r => r.Causal.DateUtc).AddDays (1);
			var pullbackModel = pullbackTrainer.Train (pullbackSamples, asOfDate);
			var pullbackEngine = pullbackTrainer.CreateEngine (pullbackModel);

			// *** Итерируем по каждому дню и решаем, использовать ли отложенный вход A. ***
			foreach (var rec in records)
				{
				if (rec.Causal == null)
					{
					throw new InvalidOperationException (
						$"[PopulateDelayedA] BacktestRecord.Causal is null for date {rec.DateUtc:O}.");
					}

				var causal = rec.Causal;

				// Направление дневной сделки
				bool wantLong = causal.PredLabel == 2 || (causal.PredLabel == 1 && causal.PredMicroUp);
				bool wantShort = causal.PredLabel == 0 || (causal.PredLabel == 1 && causal.PredMicroDown);

				if (!wantLong && !wantShort)
					{
					// Нет торгового сигнала – это нормальная ситуация, не ошибка данных.
					causal.DelayedEntryUsed = false;
					continue;
					}

				// 1. Гейт по SL-модели: используем A только если SL-модель считает день рискованным.
				if (!causal.SlHighDecision)
					{
					causal.DelayedEntryUsed = false;
					continue;
					}

				// dayStart = NY-утро (через BacktestRecord.DateUtc)
				DateTime dayStart = rec.DateUtc;

				// t_exit (baseline) = следующее рабочее NY-утро 08:00 (минус 2 минуты) в UTC.
				DateTime dayEnd = Windowing.ComputeBaselineExitUtc (dayStart);

				bool strongSignal = (causal.PredLabel == 2 || causal.PredLabel == 0);
				double dayMinMove = causal.MinMove > 0 ? causal.MinMove : 0.02;

				// 1h внутри baseline-окна — для фич модели A
				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= dayStart && h.OpenTimeUtc < dayEnd)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();

				if (dayHours.Count == 0)
					throw new InvalidOperationException ($"[PopulateDelayedA] No 1h candles in baseline window for {dayStart:O}.");

				// Фичи модели A
				var features = TargetLevelFeatureBuilder.Build (
					dayStart,
					wantLong,
					strongSignal,
					dayMinMove,
					rec.Entry,
					dayHours
				);

				var pullbackSample = new PullbackContinuationSample
					{
					Features = features,
					Label = false,
					EntryUtc = dayStart
					};

				var predA = pullbackEngine.Predict (pullbackSample);

				// 2. Гейт по модели A
				if (!predA.PredictedLabel || predA.Probability < 0.70f)
					{
					causal.DelayedEntryUsed = false;
					continue;
					}

				// 3. Если дошли сюда — A сказала "да", SL сказал "рискованно".
				causal.DelayedSource = "A";
				causal.DelayedEntryAsked = true;
				causal.DelayedEntryUsed = true;

				// Минутки внутри baseline-окна
				var dayMinutes = sol1m
					.Where (m => m.OpenTimeUtc >= dayStart && m.OpenTimeUtc < dayEnd)
					.OrderBy (m => m.OpenTimeUtc)
					.ToList ();

				if (dayMinutes.Count == 0)
					throw new InvalidOperationException ($"[PopulateDelayedA] No 1m candles in baseline window for {dayStart:O}.");

				// Цена триггера (глубина отката = dipFrac от цены входа)
				double triggerPrice = wantLong
					? rec.Entry * (1.0 - dipFrac)
					: rec.Entry * (1.0 + dipFrac);

				// Максимальная задержка — 4 часа от dayStart
				DateTime maxDelayTime = dayStart.AddHours (4);
				Candle1m? fillBar = null;

				foreach (var m in dayMinutes)
					{
					if (m.OpenTimeUtc > maxDelayTime)
						break;

					if (wantLong && m.Low <= triggerPrice)
						{
						fillBar = m;
						break;
						}

					if (wantShort && m.High >= triggerPrice)
						{
						fillBar = m;
						break;
						}
					}

				if (fillBar == null)
					{
					// цена отката не достигнута — вход не исполнился
					rec.DelayedEntryExecuted = false;
					rec.DelayedWhyNot = "no trigger";
					continue;
					}

				// Фиксируем факт исполнения
				rec.DelayedEntryExecuted = true;
				rec.DelayedEntryExecutedAtUtc = fillBar.OpenTimeUtc;
				rec.DelayedEntryPrice = triggerPrice;

				// Привязка TP/SL к MinMove (каузальные параметры уходят в CausalPredictionRecord).
				double effectiveTpPct = tpPct;
				double effectiveSlPct = slPct;

				if (causal.MinMove > 0.0)
					{
					double linkedTp = causal.MinMove * 1.2;
					if (linkedTp > effectiveTpPct)
						effectiveTpPct = linkedTp;
					}

				causal.DelayedIntradayTpPct = effectiveTpPct;
				causal.DelayedIntradaySlPct = effectiveSlPct;

				// Уровни TP/SL от цены фактического исполнения
				double tpLevel = wantLong
					? rec.DelayedEntryPrice * (1.0 + effectiveTpPct)
					: rec.DelayedEntryPrice * (1.0 - effectiveTpPct);

				double slLevel = wantLong
					? rec.DelayedEntryPrice * (1.0 - effectiveSlPct)
					: rec.DelayedEntryPrice * (1.0 + effectiveSlPct);

				// Определяем, что сработало первым, в окне [ExecutedAt; dayEnd)
				rec.DelayedIntradayResult = (int) DelayedIntradayResult.None;

				foreach (var m in dayMinutes)
					{
					if (m.OpenTimeUtc < rec.DelayedEntryExecutedAtUtc)
						continue;

					bool hitTp = wantLong ? (m.High >= tpLevel) : (m.Low <= tpLevel);
					bool hitSl = wantLong ? (m.Low <= slLevel) : (m.High >= slLevel);

					if (hitTp && hitSl)
						{
						rec.DelayedIntradayResult = (int) DelayedIntradayResult.Ambiguous;
						break;
						}

					if (hitTp)
						{
						rec.DelayedIntradayResult = (int) DelayedIntradayResult.TpFirst;
						break;
						}

					if (hitSl)
						{
						rec.DelayedIntradayResult = (int) DelayedIntradayResult.SlFirst;
						break;
						}
					}
				}
			}
		}
	}
