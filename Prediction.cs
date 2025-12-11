using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Глобальная граница train-периода для дневной модели.
		/// Всё, что ≤ этой даты, используется только для обучения.
		/// Всё, что > этой даты, считается OOS (для логов/аналитики).
		/// </summary>
		private static DateTime _trainUntilUtc;

		/// <summary>
		/// Создаёт PredictionEngine:
		/// - выбирает обучающую часть истории (train) по дате;
		/// - тренирует дневной move+dir и микро-слой через ModelTrainer;
		/// - запоминает границу train-периода в _trainUntilUtc.
		/// </summary>
		private static PredictionEngine CreatePredictionEngineOrFallback ( List<DataRow> allRows )
			{
			PredictionEngine.DebugAllowDisabledModels = false;

			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (allRows.Count == 0)
				throw new InvalidOperationException ("[engine] Пустой список DataRow для обучения моделей");

			var ordered = allRows
				.OrderBy (r => r.Date)
				.ToList ();

			var minDate = ordered.First ().Date;
			var maxDate = ordered.Last ().Date;

			// Простой временной hold-out: последние N дней не участвуют в обучении,
			// чтобы модели не видели самые свежие дни, по которым считаются forward-метрики.
			const int HoldoutDays = 120;
			var trainUntil = maxDate.AddDays (-HoldoutDays);

			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			// --- Диагностика распределения таргетов на train ---
			var labelHist = trainRows
				.GroupBy (r => r.Label)
				.OrderBy (g => g.Key)
				.Select (g => $"{g.Key}={g.Count ()}")
				.ToArray ();

			Console.WriteLine ("[engine] train label hist: " + string.Join (", ", labelHist));

			if (labelHist.Length <= 1)
				{
				Console.WriteLine (
					"[engine] WARNING: train labels are degenerate (<=1 class). " +
					"Daily model will inevitably be constant.");
				}

			if (trainRows.Count < 100)
				{
				// Если данных мало — лучше обучиться на всём, чем падать.
				Console.WriteLine (
					$"[engine] trainRows too small ({trainRows.Count}), " +
					"используем всю историю без hold-out (метрики будут train-like)");

				trainRows = ordered;
				// по сути, train == вся история → OOS нет
				_trainUntilUtc = ordered.Last ().Date;
				}
			else
				{
				_trainUntilUtc = trainUntil;

				Console.WriteLine (
					$"[engine] training on rows <= {trainUntil:yyyy-MM-dd} " +
					$"({trainRows.Count} из {ordered.Count}, диапазон [{minDate:yyyy-MM-dd}; {trainUntil:yyyy-MM-dd}])");
				}

			var trainer = new ModelTrainer
				{
				DisableMoveModel = false,           // отключаем move
				DisableDirNormalModel = false,
				DisableDirDownModel = false,
				DisableMicroFlatModel = false
				};
			var bundle = trainer.TrainAll (trainRows);

			if (bundle.MlCtx == null)
				throw new InvalidOperationException ("[engine] ModelTrainer вернул ModelBundle с MlCtx == null");

			/*if (bundle.MoveModel == null)
				throw new InvalidOperationException ("[engine] ModelBundle.MoveModel == null после обучения");*/

			//if (bundle.DirModelNormal == null && bundle.DirModelDown == null)
			//	throw new InvalidOperationException (
			//		"[engine] Оба направления (DirModelNormal/DirModelDown) == null после обучения");

			Console.WriteLine (
				"[engine] ModelBundle trained: move+dir " +
				(bundle.MicroFlatModel != null ? "+ micro" : "(без микро-слоя, микро будет выключен)"));

			return new PredictionEngine (bundle);
			}

		/// <summary>
		/// Строит PredictionRecord'ы для ВСЕХ mornings (train + OOS).
		/// Граница _trainUntilUtc:
		/// - задаёт разделение train/OOS;
		/// - используется в логах и последующей аналитике,
		///   но не режет список для стратегий/бэктеста.
		/// </summary>
		private static async Task<List<BacktestRecord>> LoadPredictionRecordsAsync (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<Candle6h> solAll6h,
			PredictionEngine engine )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (engine == null) throw new ArgumentNullException (nameof (engine));

			if (_trainUntilUtc == default)
				{
				throw new InvalidOperationException (
					"[forward] _trainUntilUtc не установлен. " +
					"Сначала должен быть вызван CreatePredictionEngineOrFallback.");
				}

			var sorted6h = solAll6h is List<Candle6h> list6h ? list6h : solAll6h.ToList ();
			if (sorted6h.Count == 0)
				throw new InvalidOperationException ("[forward] Пустая серия 6h для SOL");

			var indexByOpenTime = new Dictionary<DateTime, int> (sorted6h.Count);
			for (int i = sorted6h.Count - 1; i >= 0; i--)
				{
				var openTime = sorted6h[i].OpenTimeUtc;
				indexByOpenTime[openTime] = i;
				}

			var orderedMornings = mornings as List<DataRow> ?? mornings.ToList ();

			var oosCount = orderedMornings.Count (r => r.Date > _trainUntilUtc);
			Console.WriteLine (
				$"[forward] mornings total = {orderedMornings.Count}, " +
				$"OOS (Date > trainUntil={_trainUntilUtc:yyyy-MM-dd}) = {oosCount}");

			if (oosCount == 0)
				{
				Console.WriteLine (
					$"[forward] предупреждение: нет out-of-sample mornings " +
					$"(все дни <= trainUntil={_trainUntilUtc:O}). " +
					"Стратегические метрики будут train-like.");
				}

			static int ArgmaxLabel ( double pUp, double pFlat, double pDown )
				{
				if (pUp >= pFlat && pUp >= pDown) return 2;
				if (pDown >= pFlat && pDown >= pUp) return 0;
				return 1;
				}

			var list = new List<BacktestRecord> (orderedMornings.Count);

			foreach (var r in orderedMornings)
				{
				// 1. Каузальное предсказание.
				var pr = engine.Predict (r);
				var cls = pr.Class;
				var microUp = pr.Micro.ConsiderUp;
				var microDn = pr.Micro.ConsiderDown;
				var reason = pr.Reason;

				var day = pr.Day;
				var dayWithMicro = pr.DayWithMicro;

				var predLabelDay = ArgmaxLabel (day.PUp, day.PFlat, day.PDown);
				var predLabelDayMicro = ArgmaxLabel (dayWithMicro.PUp, dayWithMicro.PFlat, dayWithMicro.PDown);

				// На этом этапе Total = Day+Micro, SL-оверлей может обновить позже.
				double probUpTotal = dayWithMicro.PUp;
				double probFlatTotal = dayWithMicro.PFlat;
				double probDownTotal = dayWithMicro.PDown;
				int predLabelTotal = predLabelDayMicro;

				// 2. Forward-окно по 6h-свечам.
				var entryUtc = r.Date;
				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz);

				if (!indexByOpenTime.TryGetValue (entryUtc, out var entryIdx))
					{
					throw new InvalidOperationException (
						$"[forward] entry candle {entryUtc:O} not found in 6h series");
					}

				var exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					var end = (i + 1 < sorted6h.Count)
						? sorted6h[i + 1].OpenTimeUtc
						: start.AddHours (6);

					if (exitUtc >= start && exitUtc < end)
						{
						exitIdx = i;
						break;
						}
					}

				if (exitIdx < 0)
					{
					throw new InvalidOperationException (
						$"[forward] no 6h candle covering baseline exit {exitUtc:O} (entry {entryUtc:O})");
					}

				if (exitIdx <= entryIdx)
					{
					throw new InvalidOperationException (
						$"[forward] exitIdx {exitIdx} <= entryIdx {entryIdx} для entry {entryUtc:O}");
					}

				var entryPrice = sorted6h[entryIdx].Close;

				double maxHigh = double.MinValue;
				double minLow = double.MaxValue;

				for (int j = entryIdx + 1; j <= exitIdx; j++)
					{
					var c = sorted6h[j];

					if (c.High > maxHigh) maxHigh = c.High;
					if (c.Low < minLow) minLow = c.Low;
					}

				if (maxHigh == double.MinValue || minLow == double.MaxValue)
					{
					throw new InvalidOperationException (
						$"[forward] no candles between entry {entryUtc:O} and exit {exitUtc:O}");
					}

				var fwdClose = sorted6h[exitIdx].Close;

				// 3. Сборка каузальной части.
				var causal = new CausalPredictionRecord
					{
					DateUtc = r.Date,

					TrueLabel = r.Label,
					PredLabel = cls,

					PredLabel_Day = predLabelDay,
					PredLabel_DayMicro = predLabelDayMicro,
					PredLabel_Total = predLabelTotal,

					ProbUp_Day = day.PUp,
					ProbFlat_Day = day.PFlat,
					ProbDown_Day = day.PDown,

					ProbUp_DayMicro = dayWithMicro.PUp,
					ProbFlat_DayMicro = dayWithMicro.PFlat,
					ProbDown_DayMicro = dayWithMicro.PDown,

					ProbUp_Total = probUpTotal,
					ProbFlat_Total = probFlatTotal,
					ProbDown_Total = probDownTotal,

					Conf_Day = day.Confidence,
					Conf_Micro = dayWithMicro.Confidence,

					MicroPredicted = microUp || microDn,
					PredMicroUp = microUp,
					PredMicroDown = microDn,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					RegimeDown = r.RegimeDown,
					Reason = reason,
					MinMove = r.MinMove,

					SlProb = 0.0,
					SlHighDecision = false,
					Conf_SlLong = 0.0,
					Conf_SlShort = 0.0,

					DelayedSource = null,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,
					TargetLevelClass = 0
					};

				// 4. Сборка forward-части.
				var forward = new ForwardOutcomes
					{
					Entry = entryPrice,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,
					MinMove = r.MinMove,
					WindowEndUtc = exitUtc,
					DayMinutes = Array.Empty<Candle1m> ()
					};

				// 5. Финальный BacktestRecord.
				list.Add (new BacktestRecord
					{
					Causal = causal,
					Forward = forward
					});
				}

			return await Task.FromResult (list);
			}
		}
	}
