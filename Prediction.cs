using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Глобальная граница train-периода дневной модели в терминах baseline-exit.
		/// Любая классификация train/OOS должна происходить через TrainBoundary (baseline-exit контракт),
		/// а не через ручные сравнения entry-даты.
		/// </summary>
		private static DateTime _trainUntilUtc;

		/// <summary>
		/// Создаёт PredictionEngine:
		/// - выбирает train-часть истории через TrainBoundary (baseline-exit);
		/// - тренирует дневной move+dir и микро-слой;
		/// - фиксирует границу train-периода в _trainUntilUtc (baseline-exit).
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

			var minEntryUtc = ordered.First ().Date;
			var maxEntryUtc = ordered.Last ().Date;

			// Простой временной hold-out: последние N дней (по entry) исключаем из train.
			// ВАЖНО: сама граница хранится/применяется в терминах baseline-exit, чтобы не было boundary leakage.
			const int HoldoutDays = 120;

			var trainUntilUtc = DeriveTrainUntilUtcFromHoldout (
				maxEntryUtc: maxEntryUtc,
				holdoutDays: HoldoutDays,
				nyTz: NyTz);

			var boundary = new TrainBoundary (trainUntilUtc, NyTz);

			// Train/OOS режем только через baseline-exit контракт.
			var split = boundary.Split (ordered, r => r.Date);
			var trainRows = split.Train;

			if (split.Excluded.Count > 0)
				{
				// Эти дни нельзя “молча” относить к train/OOS (baseline-окно по контракту не определено).
				// В прод-пайплайне такие строки обычно вообще не должны существовать.
				Console.WriteLine (
					$"[engine] WARNING: excluded={split.Excluded.Count} rows because baseline-exit is undefined (weekend by contract). " +
					"Проверь генерацию daily-строк/окон.");
				}

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
				// Границу выставляем как максимум baseline-exit по всей доступной истории,
				// чтобы дальнейшие метрики/аналитика использовали тот же контракт.
				Console.WriteLine (
					$"[engine] trainRows too small ({trainRows.Count}), " +
					"используем всю историю без hold-out (метрики будут train-like)");

				trainRows = ordered;

				_trainUntilUtc = DeriveMaxBaselineExitUtc (
					rows: ordered,
					nyTz: NyTz);
				}
			else
				{
				_trainUntilUtc = trainUntilUtc;

				Console.WriteLine (
					$"[engine] training on rows with baseline-exit <= {boundary.TrainUntilIsoDate} " +
					$"(train={split.Train.Count}, oos={split.Oos.Count}, total={ordered.Count}, entryRange=[{minEntryUtc:yyyy-MM-dd}; {maxEntryUtc:yyyy-MM-dd}])");
				}

			var trainer = new ModelTrainer
				{
				DisableMoveModel = false,
				DisableDirNormalModel = false,
				DisableDirDownModel = false,
				DisableMicroFlatModel = false
				};

			var bundle = trainer.TrainAll (trainRows);

			if (bundle.MlCtx == null)
				throw new InvalidOperationException ("[engine] ModelTrainer вернул ModelBundle с MlCtx == null");

			Console.WriteLine (
				"[engine] ModelBundle trained: move+dir " +
				(bundle.MicroFlatModel != null ? "+ micro" : "(без микро-слоя, микро будет выключен)"));

			return new PredictionEngine (bundle);
			}

		/// <summary>
		/// Строит BacktestRecord'ы для ВСЕХ mornings (train + OOS).
		/// Разбиение train/OOS — строго через TrainBoundary (baseline-exit).
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

			var boundary = new TrainBoundary (_trainUntilUtc, NyTz);

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

			// Диагностика сплита: только через baseline-exit контракт.
			var split = boundary.Split (orderedMornings, r => r.Date);

			Console.WriteLine (
				$"[forward] mornings total={orderedMornings.Count}, " +
				$"train={split.Train.Count}, oos={split.Oos.Count}, excluded={split.Excluded.Count}, " +
				$"trainUntil(baseline-exit)={boundary.TrainUntilIsoDate}");

			if (split.Oos.Count == 0)
				{
				Console.WriteLine (
					"[forward] предупреждение: нет out-of-sample mornings по baseline-exit контракту. " +
					"Стратегические метрики будут train-like.");
				}

			if (split.Excluded.Count > 0)
				{
				Console.WriteLine (
					"[forward] WARNING: есть excluded-дни, для которых baseline-exit не определён (weekend по контракту). " +
					"Такие дни будут пропущены при сборке records.");
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
				var entryUtc = r.Date;

				// Baseline-exit по контракту.
				if (!boundary.TryGetBaselineExitUtc (entryUtc, out var exitUtc))
					{
					// Нельзя “молча” классифицировать такие дни как train/OOS.
					Console.WriteLine (
						$"[forward] skip entry {entryUtc:O}: baseline-exit undefined by contract (weekend).");
					continue;
					}

				// 1) Каузальное предсказание.
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

				// 2) Forward-окно по 6h-свечам.
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

				// 3) Каузальная часть.
				var causal = new CausalPredictionRecord
					{
					DateUtc = entryUtc,

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

				// 4) Forward-часть.
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

				// 5) Финальный BacktestRecord.
				list.Add (new BacktestRecord
					{
					Causal = causal,
					Forward = forward
					});
				}

			return await Task.FromResult (list);
			}

		/// <summary>
		/// Преобразует "holdout по entry" в границу trainUntilUtc в терминах baseline-exit.
		/// Это нужно, чтобы последующая классификация train/OOS была согласована с таргетом/forward-окном.
		/// </summary>
		private static DateTime DeriveTrainUntilUtcFromHoldout ( DateTime maxEntryUtc, int holdoutDays, TimeZoneInfo nyTz )
			{
			if (holdoutDays < 0) throw new ArgumentOutOfRangeException (nameof (holdoutDays));
			if (maxEntryUtc == default) throw new ArgumentException ("maxEntryUtc must be initialized.", nameof (maxEntryUtc));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var candidateEntry = maxEntryUtc.AddDays (-holdoutDays);

			// Если внезапно попали на weekend — отступаем назад до ближайшего рабочего entry-дня.
			for (int i = 0; i < 14; i++)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (candidateEntry, nyTz);
				if (ny.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
					{
					return Windowing.ComputeBaselineExitUtc (candidateEntry, nyTz);
					}

				candidateEntry = candidateEntry.AddDays (-1);
				}

			throw new InvalidOperationException (
				"[engine] failed to derive trainUntilUtc from holdout: too many consecutive non-working days. " +
				"Проверь корректность entry-дат и timezone.");
			}

		/// <summary>
		/// Максимальный baseline-exit по заданному списку дней.
		/// Используется как "граница" в режиме train==full-history, чтобы метрики не делали ручных <=/>.
		/// </summary>
		private static DateTime DeriveMaxBaselineExitUtc ( IReadOnlyList<DataRow> rows, TimeZoneInfo nyTz )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (rows.Count == 0) throw new ArgumentException ("rows must be non-empty.", nameof (rows));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			bool hasAny = false;
			DateTime maxExit = default;

			for (int i = 0; i < rows.Count; i++)
				{
				var entryUtc = rows[i].Date;
				var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, nyTz);

				// По контракту weekend baseline-окна не имеют; такие строки должны отсутствовать.
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					{
					continue;
					}

				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

				if (!hasAny || exitUtc > maxExit)
					{
					maxExit = exitUtc;
					hasAny = true;
					}
				}

			if (!hasAny)
				{
				throw new InvalidOperationException (
					"[engine] failed to derive max baseline-exit: no working-day entries found.");
				}

			return maxExit;
			}
		}
	}
