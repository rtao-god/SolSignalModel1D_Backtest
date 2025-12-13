using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Tests.Leakage.Old;
using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Пайплайновые тесты на утечки:
	/// 1) shuffle train-лейблов → OOS-качество должно сильно просесть;
	/// 2) randomize train-фичей → OOS-качество должно просесть до околослучайного уровня.
	/// </summary>
	public sealed class DailyPipelineLeakageTests
		{
		private sealed class DailyRunResult
			{
			public required DateTime TrainUntilUtc { get; init; }
			public required List<BacktestRecord> Records { get; init; }
			}

		[Fact]
		public async Task DailyModel_OosQualityDrops_WhenTrainLabelsAreShuffled ()
			{
			var (allRows, mornings, solAll6h, _, _) =
				await LegacyTargetSanityTests.BootstrapRowsAndCandlesAsync ();

			var baseline = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: null);

			var accBaseline = ComputeOosAccuracy (baseline.Records, baseline.TrainUntilUtc);
			Assert.False (double.IsNaN (accBaseline), "Baseline OOS accuracy is NaN.");

			var shuffled = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: ( rows, trainUntil ) =>
					ShuffleTrainLabels (rows, trainUntil, seed: 123));

			var accShuffled = ComputeOosAccuracy (shuffled.Records, shuffled.TrainUntilUtc);

			Assert.True (
				accShuffled < accBaseline - 0.15,
				$"OOS accuracy with shuffled labels did not drop enough. baseline={accBaseline:0.000}, shuffled={accShuffled:0.000}");
			}

		[Fact]
		public async Task DailyModel_OosQualityDrops_WhenTrainFeaturesAreRandomized ()
			{
			var (allRows, mornings, solAll6h, _, _) =
				await LegacyTargetSanityTests.BootstrapRowsAndCandlesAsync ();

			var baseline = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: null);

			var accBaseline = ComputeOosAccuracy (baseline.Records, baseline.TrainUntilUtc);
			Assert.False (double.IsNaN (accBaseline), "Baseline OOS accuracy is NaN.");

			var randomized = await RunDailyPipelineAsync (
				allRows,
				mornings,
				solAll6h,
				mutateTrain: ( rows, trainUntil ) =>
					RandomizeTrainFeatures (rows, trainUntil, seed: 42));

			var accRandom = ComputeOosAccuracy (randomized.Records, randomized.TrainUntilUtc);

			Assert.True (
				accRandom < accBaseline - 0.15 && accRandom < 0.5,
				$"OOS accuracy with randomized features is suspiciously high. baseline={accBaseline:0.000}, randomized={accRandom:0.000}");
			}

		private static async Task<DailyRunResult> RunDailyPipelineAsync (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h,
			Action<List<DataRow>, DateTime>? mutateTrain )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));

			var prevDebug = PredictionEngine.DebugAllowDisabledModels;
			PredictionEngine.DebugAllowDisabledModels = true;

			try
				{
				var ordered = allRows
					.OrderBy (r => r.Date)
					.ToList ();

				if (ordered.Count == 0)
					throw new InvalidOperationException ("RunDailyPipelineAsync: пустой allRows.");

				var maxDate = ordered[^1].Date;

				const int HoldoutDays = 120;
				var trainUntil = maxDate.AddDays (-HoldoutDays);

				var trainRows = TakeTrainRows (ordered, trainUntil);

				if (trainRows.Count < 100)
					{
					// Мало данных — учимся на всей истории.
					trainRows = ordered;
					trainUntil = ordered[^1].Date;
					}

				// В этой точке уже известна финальная граница trainUntil.
				// Даём тесту возможность мутировать train-часть (лейблы/фичи) до TrainAll.
				mutateTrain?.Invoke (allRows, trainUntil);

				// После мутации пересобираем trainRows детерминированно (без LINQ-сплита по всему проекту).
				ordered = allRows.OrderBy (r => r.Date).ToList ();
				trainRows = TakeTrainRows (ordered, trainUntil);

				var trainer = new ModelTrainer
					{
					DisableMoveModel = false,
					DisableDirNormalModel = false,
					DisableDirDownModel = true,
					DisableMicroFlatModel = false
					};

				var bundle = trainer.TrainAll (trainRows);

				if (bundle.MlCtx == null)
					throw new InvalidOperationException ("ModelTrainer вернул ModelBundle с MlCtx == null.");

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
			finally
				{
				PredictionEngine.DebugAllowDisabledModels = prevDebug;
				}
			}

		private static List<DataRow> TakeTrainRows ( List<DataRow> orderedByDate, DateTime trainUntilUtc )
			{
			var train = new List<DataRow> (orderedByDate.Count);

			// orderedByDate гарантированно отсортирован: можно останавливаться на первом OOS.
			foreach (var r in orderedByDate)
				{
				if (r.Date <= trainUntilUtc)
					train.Add (r);
				else
					break;
				}

			return train;
			}

		private static async Task<List<BacktestRecord>> BuildPredictionRecordsAsyncForTests (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<Candle6h> solAll6h,
			PredictionEngine engine )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (engine == null) throw new ArgumentNullException (nameof (engine));

			static (double Up, double Flat, double Down) MakeTriProbs ( int predLabel )
				{
				const double Hi = 0.90;
				const double Lo = 0.05;

				return predLabel switch
					{
						2 => (Hi, Lo, Lo),
						1 => (Lo, Hi, Lo),
						0 => (Lo, Lo, Hi),
						_ => throw new ArgumentOutOfRangeException (nameof (predLabel), predLabel, "PredLabel must be in [0..2].")
						};
				}

			var sorted6h = solAll6h is List<Candle6h> list6h
				? list6h
				: solAll6h.ToList ();

			if (sorted6h.Count == 0)
				throw new InvalidOperationException ("[forward-pipeline] Пустая серия 6h для SOL.");

			var indexByOpenTime = new Dictionary<DateTime, int> (sorted6h.Count);
			for (int i = sorted6h.Count - 1; i >= 0; i--)
				indexByOpenTime[sorted6h[i].OpenTimeUtc] = i;

			var nyTz = Windowing.NyTz;
			var list = new List<BacktestRecord> (mornings.Count);

			foreach (var r in mornings)
				{
				var pr = engine.Predict (r);

				var entryUtc = r.Date;
				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

				if (!indexByOpenTime.TryGetValue (entryUtc, out var entryIdx))
					throw new InvalidOperationException ($"[forward-pipeline] entry candle {entryUtc:O} not found in 6h series.");

				var exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					var end = i + 1 < sorted6h.Count ? sorted6h[i + 1].OpenTimeUtc : start.AddHours (6);

					if (exitUtc >= start && exitUtc < end)
						{
						exitIdx = i;
						break;
						}
					}

				if (exitIdx < 0)
					throw new InvalidOperationException ($"[forward-pipeline] no 6h candle covering baseline exit {exitUtc:O} (entry {entryUtc:O}).");

				if (exitIdx <= entryIdx)
					throw new InvalidOperationException ($"[forward-pipeline] exitIdx {exitIdx} <= entryIdx {entryIdx} для entry {entryUtc:O}.");

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
					throw new InvalidOperationException ($"[forward-pipeline] no candles between entry {entryUtc:O} and exit {exitUtc:O}.");

				var fwdClose = sorted6h[exitIdx].Close;

				var (pUp, pFlat, pDown) = MakeTriProbs (pr.Class);

				var causal = new CausalPredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,

					PredLabel = pr.Class,
					PredLabel_Day = pr.Class,
					PredLabel_DayMicro = pr.Class,

					ProbUp_Day = pUp,
					ProbFlat_Day = pFlat,
					ProbDown_Day = pDown,

					ProbUp_DayMicro = pUp,
					ProbFlat_DayMicro = pFlat,
					ProbDown_DayMicro = pDown,

					ProbUp_Total = pUp,
					ProbFlat_Total = pFlat,
					ProbDown_Total = pDown,

					Conf_Day = Math.Max (pUp, Math.Max (pFlat, pDown)),
					Conf_Micro = 0.0,

					MicroPredicted = pr.Micro.ConsiderUp || pr.Micro.ConsiderDown,
					PredMicroUp = pr.Micro.ConsiderUp,
					PredMicroDown = pr.Micro.ConsiderDown,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					RegimeDown = r.RegimeDown,
					Reason = pr.Reason,
					MinMove = r.MinMove,

					SlProb = 0.0,
					SlHighDecision = false,

					DelayedSource = null,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,
					TargetLevelClass = 0
					};

				var forward = new ForwardOutcomes
					{
					DateUtc = r.Date,
					WindowEndUtc = exitUtc,

					Entry = entryPrice,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,

					MinMove = r.MinMove,
					DayMinutes = Array.Empty<Candle1m> ()
					};

				list.Add (new BacktestRecord
					{
					Causal = causal,
					Forward = forward,

					AntiDirectionApplied = false,
					DelayedEntryExecuted = false,
					DelayedEntryPrice = 0.0,
					DelayedEntryExecutedAtUtc = null,
					DelayedIntradayResult = 0,
					DelayedWhyNot = null
					});
				}

			return await Task.FromResult (list);
			}

		private static double ComputeOosAccuracy ( List<BacktestRecord> records, DateTime trainUntilUtc )
			{
			int total = 0;
			int ok = 0;

			foreach (var r in records)
				{
				if (r.Causal.DateUtc <= trainUntilUtc)
					continue;

				total++;
				if (r.Causal.PredLabel == r.Causal.TrueLabel)
					ok++;
				}

			return total == 0 ? double.NaN : ok / (double) total;
			}

		private static void ShuffleTrainLabels ( List<DataRow> rows, DateTime trainUntilUtc, int seed )
			{
			var train = new List<DataRow> (rows.Count);
			foreach (var r in rows)
				{
				if (r.Date <= trainUntilUtc)
					train.Add (r);
				}

			if (train.Count == 0)
				throw new InvalidOperationException ("[shuffle-labels] train-set is empty.");

			var labels = train.Select (r => r.Label).ToArray ();

			var rng = new Random (seed);
			for (int i = labels.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(labels[i], labels[j]) = (labels[j], labels[i]);
				}

			for (int i = 0; i < train.Count; i++)
				train[i].Label = labels[i];
			}

		private static void RandomizeTrainFeatures ( List<DataRow> rows, DateTime trainUntilUtc, int seed )
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
