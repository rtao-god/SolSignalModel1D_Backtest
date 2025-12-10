using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Tests.Leakage.Old;
using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Пайплайновые тесты на утечки:
	/// 1) shuffle train-лейблов → OOS-качество должно сильно просесть;
	/// 2) randomize train-фичей → OOS-качество должно просесть до околослучайного уровня.
	/// Используют тот же Bootstrap, что и LegacyTargetSanityTests, но с дополнительными мутациями train-части.
	/// </summary>
	public sealed class DailyPipelineLeakageTests
		{
		private sealed class DailyRunResult
			{
			public required DateTime TrainUntilUtc { get; init; }
			public required List<PredictionRecord> Records { get; init; }
			}

		// =====================================================================
		// ПУБЛИЧНЫЕ ТЕСТЫ
		// =====================================================================

		[Fact]
		public async Task DailyModel_OosQualityDrops_WhenTrainLabelsAreShuffled ()
			{
			// Один Bootstrap на тест: дальше переиспользуем те же ряды
			var (allRows, mornings, solAll6h, _, _) =
				await LegacyTargetSanityTests.BootstrapRowsAndCandlesAsync ();

			// Базовый прогон без вмешательства
			var baseline = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: null);

			var accBaseline = ComputeOosAccuracy (baseline.Records, baseline.TrainUntilUtc);
			Assert.False (double.IsNaN (accBaseline), "Baseline OOS accuracy is NaN.");

			// Прогон с перемешанными train-лейблами на тех же данных
			var shuffled = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: ( rows, trainUntil ) =>
					ShuffleTrainLabels (rows, trainUntil, seed: 123));

			var accShuffled = ComputeOosAccuracy (shuffled.Records, shuffled.TrainUntilUtc);

			// Требование: после шурфлинга качество должно заметно упасть.
			// Порог можно подстроить под реальные метрики, идея такая:
			// если accBaseline ≈ 0.6–0.7, то accShuffled ожидается близко к 0.33.
			Assert.True (
				accShuffled < accBaseline - 0.15,
				$"OOS accuracy with shuffled labels did not drop enough. baseline={accBaseline:0.000}, shuffled={accShuffled:0.000}");
			}

		[Fact]
		public async Task DailyModel_OosQualityDrops_WhenTrainFeaturesAreRandomized ()
			{
			// Один общий Bootstrap для baseline и random-run
			var (allRows, mornings, solAll6h, _, _) =
				await LegacyTargetSanityTests.BootstrapRowsAndCandlesAsync ();

			// Базовый прогон
			var baseline = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: null);

			var accBaseline = ComputeOosAccuracy (baseline.Records, baseline.TrainUntilUtc);
			Assert.False (double.IsNaN (accBaseline), "Baseline OOS accuracy is NaN.");

			// Прогон с рандомизированными фичами в train-части на тех же данных
			var randomized = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: ( rows, trainUntil ) =>
					RandomizeTrainFeatures (rows, trainUntil, seed: 42));

			var accRandom = ComputeOosAccuracy (randomized.Records, randomized.TrainUntilUtc);

			// Требование: качество на рандом-фичах должно быть явно хуже базового
			// и находиться в области «почти случайно».
			Assert.True (
				accRandom < accBaseline - 0.15 && accRandom < 0.5,
				$"OOS accuracy with randomized features is suspiciously high. baseline={accBaseline:0.000}, randomized={accRandom:0.000}");
			}

		// =====================================================================
		// ОБЩИЙ ПАЙПЛАЙН: Train + Forward
		// =====================================================================

		/// <summary>
		/// Мини-пайплайн дневной модели, повторяющий прод:
		/// - trainUntil = maxDate - 120d (с fallback на train=вся история при малом датасете),
		/// - ModelTrainer.TrainAll,
		/// - PredictionEngine,
		/// - forward до baseline-exit по 6h.
		/// В точке mutateTrain можно мутировать train-часть (лейблы/фичи) перед TrainAll.
		/// </summary>
		private static async Task<DailyRunResult> RunDailyPipelineAsync (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h,
			Action<List<DataRow>, DateTime>? mutateTrain )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));

			PredictionEngine.DebugAllowDisabledModels = true;

			var ordered = allRows
				.OrderBy (r => r.Date)
				.ToList ();

			if (ordered.Count == 0)
				throw new InvalidOperationException ("RunDailyPipelineAsync: пустой allRows.");

			var maxDate = ordered[^1].Date;

			const int HoldoutDays = 120;
			var trainUntil = maxDate.AddDays (-HoldoutDays);

			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			if (trainRows.Count < 100)
				{
				// Мало данных — учимся на всей истории, как в проде.
				trainRows = ordered;
				trainUntil = ordered[^1].Date;
				}

			// В этой точке уже известна финальная граница trainUntil.
			// Даём тесту возможность мутировать train-часть (лейблы/фичи).
			mutateTrain?.Invoke (allRows, trainUntil);

			// После мутации переподбираем trainRows (на случай, если что-то изменилось).
			trainRows = allRows
				.Where (r => r.Date <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			var trainer = new ModelTrainer
				{
				DisableMoveModel = false,
				DisableDirNormalModel = false,
				DisableDirDownModel = true,
				DisableMicroFlatModel = false
				};

			var bundle = trainer.TrainAll (trainRows);

			if (bundle.MlCtx == null)
				throw new InvalidOperationException (
					"ModelTrainer вернул ModelBundle с MlCtx == null.");

			var engine = new PredictionEngine (bundle);

			var records = await BuildPredictionRecordsAsyncForTests (
				mornings,
				solAll6h,
				engine);

			return new DailyRunResult
				{
				TrainUntilUtc = trainUntil,
				Records = records
				};
			}

		/// <summary>
		/// Forward-часть: строит PredictionRecord'ы для всех mornings
		/// через PredictionEngine и 6h-свечи до baseline-exit.
		/// Это копия логики из LegacyTargetSanityTests.
		/// </summary>
		private static async Task<List<PredictionRecord>> BuildPredictionRecordsAsyncForTests (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<Candle6h> solAll6h,
			PredictionEngine engine )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (engine == null) throw new ArgumentNullException (nameof (engine));

			var sorted6h = solAll6h is List<Candle6h> list6h
				? list6h
				: solAll6h.ToList ();

			if (sorted6h.Count == 0)
				throw new InvalidOperationException (
					"[forward-pipeline] Пустая серия 6h для SOL.");

			var indexByOpenTime = new Dictionary<DateTime, int> (sorted6h.Count);
			for (int i = sorted6h.Count - 1; i >= 0; i--)
				indexByOpenTime[sorted6h[i].OpenTimeUtc] = i;

			var nyTz = Windowing.NyTz;
			var list = new List<PredictionRecord> (mornings.Count);

			foreach (var r in mornings)
				{
				var pr = engine.Predict (r);

				var entryUtc = r.Date;
				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

				if (!indexByOpenTime.TryGetValue (entryUtc, out var entryIdx))
					{
					throw new InvalidOperationException (
						$"[forward-pipeline] entry candle {entryUtc:O} not found in 6h series.");
					}

				var exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					var end = i + 1 < sorted6h.Count
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
						$"[forward-pipeline] no 6h candle covering baseline exit {exitUtc:O} (entry {entryUtc:O}).");
					}

				if (exitIdx <= entryIdx)
					{
					throw new InvalidOperationException (
						$"[forward-pipeline] exitIdx {exitIdx} <= entryIdx {entryIdx} для entry {entryUtc:O}.");
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
						$"[forward-pipeline] no candles between entry {entryUtc:O} and exit {exitUtc:O}.");
					}

				var fwdClose = sorted6h[exitIdx].Close;

				list.Add (new PredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,
					PredLabel = pr.Class,

					PredMicroUp = pr.Micro.ConsiderUp,
					PredMicroDown = pr.Micro.ConsiderDown,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					Entry = entryPrice,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,

					RegimeDown = r.RegimeDown,
					Reason = pr.Reason,
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

		private static double ComputeOosAccuracy (
			List<PredictionRecord> records,
			DateTime trainUntilUtc )
			{
			var oos = records
				.Where (r => r.DateUtc > trainUntilUtc)
				.ToList ();

			if (oos.Count == 0)
				return double.NaN;

			var correct = oos.Count (r => r.PredLabel == r.TrueLabel);
			return correct / (double) oos.Count;
			}

		// =====================================================================
		// МУТАТОРЫ TRAIN-ЧАСТИ
		// =====================================================================

		private static void ShuffleTrainLabels (
			List<DataRow> rows,
			DateTime trainUntilUtc,
			int seed )
			{
			var train = rows
				.Where (r => r.Date <= trainUntilUtc)
				.OrderBy (r => r.Date)
				.ToList ();

			if (train.Count == 0)
				throw new InvalidOperationException (
					"[shuffle-labels] train-set is empty.");

			var labels = train
				.Select (r => r.Label)
				.ToArray ();

			var rng = new Random (seed);
			for (int i = labels.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(labels[i], labels[j]) = (labels[j], labels[i]);
				}

			for (int i = 0; i < train.Count; i++)
				train[i].Label = labels[i];
			}

		private static void RandomizeTrainFeatures (
			List<DataRow> rows,
			DateTime trainUntilUtc,
			int seed )
			{
			var rng = new Random (seed);

			foreach (var r in rows)
				{
				if (r.Date > trainUntilUtc)
					continue;

				var feats = r.Features;
				if (feats == null || feats.Length == 0)
					continue;

				for (int i = 0; i < feats.Length; i++)
					feats[i] = rng.NextDouble () * 2.0 - 1.0; // равномерно [-1;1]
				}
			}
		}
	}
