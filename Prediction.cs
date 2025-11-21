using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
	{
	internal partial class Program
		{
		/// <summary>
		/// Глобальная граница train-периода для дневной модели.
		/// Всё, что ≤ этой даты, используется только для обучения.
		/// Всё, что &gt; этой даты, идёт в PredictionRecords и метрики.
		/// </summary>
		private static DateTime _trainUntilUtc;

		/// <summary>
		/// Создаёт PredictionEngine:
		/// - выбирает обучающую часть истории (train) по дате;
		/// - тренирует дневной move+dir и микро-слой через ModelTrainer;
		/// - запоминает границу train-периода в _trainUntilUtc;
		/// - никаких fallback: если моделей нет, сразу исключение.
		/// </summary>
		private static PredictionEngine CreatePredictionEngineOrFallback ( List<DataRow> allRows )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (allRows.Count == 0)
				throw new InvalidOperationException ("[engine] Пустой список DataRow для обучения моделей");

			var ordered = allRows
				.OrderBy (r => r.Date)
				.ToList ();

			DateTime minDate = ordered.First ().Date;
			DateTime maxDate = ordered.Last ().Date;

			// Простой временной hold-out: последние N дней не участвуют в обучении,
			// чтобы модели не видели самые свежие дни, по которым считаются метрики.
			const int HoldoutDays = 120;

			DateTime trainUntil = maxDate.AddDays (-HoldoutDays);

			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			if (trainRows.Count < 100)
				{
				// Если данных мало — лучше обучиться на всём, чем падать.
				Console.WriteLine (
					$"[engine] trainRows too small ({trainRows.Count}), " +
					$"используем всю историю без hold-out (метрики будут train-like)");
				trainRows = ordered;
				_trainUntilUtc = ordered.Last ().Date; // по сути, train == вся история → OOS нет
				}
			else
				{
				_trainUntilUtc = trainUntil;

				Console.WriteLine (
					$"[engine] training on rows &lt;= {trainUntil:yyyy-MM-dd} " +
					$"({trainRows.Count} из {ordered.Count}, диапазон [{minDate:yyyy-MM-dd}; {trainUntil:yyyy-MM-dd}])");
				}

			var trainer = new ModelTrainer ();
			var bundle = trainer.TrainAll (trainRows);

			if (bundle.MlCtx == null)
				throw new InvalidOperationException ("[engine] ModelTrainer вернул ModelBundle с MlCtx == null");

			if (bundle.MoveModel == null)
				throw new InvalidOperationException ("[engine] ModelBundle.MoveModel == null после обучения");

			if (bundle.DirModelNormal == null && bundle.DirModelDown == null)
				throw new InvalidOperationException (
					"[engine] Оба направления (DirModelNormal/DirModelDown) == null после обучения");

			Console.WriteLine (
				"[engine] ModelBundle trained: move+dir " +
				(bundle.MicroFlatModel != null ? "+ micro" : "(без микро-слоя, микро будет выключен)"));

			return new PredictionEngine (bundle);
			}

		/// <summary>
		/// Строит PredictionRecord'ы ТОЛЬКО для out-of-sample дней:
		/// для тех mornings, у которых r.Date &gt; _trainUntilUtc.
		/// Train-период используется только для обучения и не попадает в метрики.
		/// </summary>
		private static async Task<List<PredictionRecord>> LoadPredictionRecordsAsync (
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

			// Подготавливаем отсортированный список 6h-свечей для forward-метрик.
			var sorted6h = solAll6h is List<Candle6h> list6h
				? list6h
				: solAll6h.ToList ();

			if (sorted6h.Count == 0)
				throw new InvalidOperationException ("[forward] Пустая серия 6h для SOL");

			// OOS-морнинги: только дни, которые НЕ использовались в обучении дневной модели.
			var oosMornings = mornings
				.Where (r => r.Date > _trainUntilUtc)
				.OrderBy (r => r.Date)
				.ToList ();

			if (oosMornings.Count == 0)
				{
				Console.WriteLine (
					$"[forward] нет out-of-sample mornings: все дни &lt;= trainUntil={_trainUntilUtc:O}. " +
					"Метрики будут пустые/некорректные.");
				}

			var list = new List<PredictionRecord> (oosMornings.Count);

			foreach (var r in oosMornings)
				{
				// Предсказание только через PredictionEngine (дневная модель + микро).
				// Никаких эвристик и ручных подмен.
				var pr = engine.Predict (r);

				int cls = pr.Class;
				bool microUp = pr.Micro.ConsiderUp;
				bool microDn = pr.Micro.ConsiderDown;
				string reason = pr.Reason;

				// Вычисляем показатели по forward-окну (до базового выхода t_exit).
				DateTime entryUtc = r.Date;

				// Общий NyTz (определён в другом partial Program), чтобы baseline-окно
				// совпадало с PnL/SL/Delayed.
				DateTime exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz);

				int entryIdx = sorted6h.FindIndex (c => c.OpenTimeUtc == entryUtc);
				if (entryIdx < 0)
					{
					throw new InvalidOperationException (
						$"[forward] entry candle {entryUtc:O} not found in 6h series");
					}

				int exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					DateTime end = (i + 1 < sorted6h.Count)
						? sorted6h[i + 1].OpenTimeUtc
						: start.AddHours (6);

					if (exitUtc >= start && exitUtc <= end)
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
						$"[forward] exitIdx {exitIdx} &lt;= entryIdx {entryIdx} для entry {entryUtc:O}");
					}

				double entryPrice = sorted6h[entryIdx].Close;
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

				double fwdClose = sorted6h[exitIdx].Close;

				list.Add (new PredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,
					PredLabel = cls,

					PredMicroUp = microUp,
					PredMicroDown = microDn,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					Entry = entryPrice,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,

					RegimeDown = r.RegimeDown,
					Reason = reason,
					MinMove = r.MinMove,

					DelayedSource = string.Empty,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedEntryExecuted = false,
					DelayedEntryPrice = 0.0,
					DelayedIntradayResult = 0,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,
					TargetLevelClass = 0,
					DelayedWhyNot = null,
					DelayedEntryExecutedAtUtc = null,

					SlProb = 0.0,
					SlHighDecision = false
					});
				}

			return await Task.FromResult (list);
			}
		}
	}
