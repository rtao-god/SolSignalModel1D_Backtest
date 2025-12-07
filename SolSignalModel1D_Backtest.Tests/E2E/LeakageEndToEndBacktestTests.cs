using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Tests.E2E
	{
	/// <summary>
	/// E2E leakage-тест: RowBuilder → Daily/SL датасеты → обучение дневной и SL-модели.
	/// Изменяем будущий хвост свечей, проверяем, что:
	/// - train-датасеты совпадают;
	/// - предсказания дневной модели на train-наборе не меняются;
	/// - предсказания SL-модели на train-наборе не меняются.
	/// </summary>
	public sealed class LeakageEndToEndBacktestTests
		{
		private sealed class BinaryOutput
			{
			public bool PredictedLabel { get; set; }
			public float Score { get; set; }
			public float Probability { get; set; }
			}

		private sealed class SlEvalRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
			}

		[Fact]
		public void EndToEnd_DailyAndSl_Training_IsFutureBlind_ToTailMutation ()
			{
			// 1. Синтетическая длинная история для RowBuilder/SL.
			BuildSyntheticHistory (
				days: 600,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
				out var sol6hDictA,
				out var fngHistory,
				out var dxyHistory);

			CloneHistory (
				solWinTrainA,
				btcWinTrainA,
				paxgWinTrainA,
				solAll6hA,
				solAll1mA,
				out var solWinTrainB,
				out var btcWinTrainB,
				out var paxgWinTrainB,
				out var solAll6hB,
				out var solAll1mB);

			// 2. RowBuilder: базовые A/B наборы DataRow.
			var rowsAAll = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrainA,
				btcWinTrain: btcWinTrainA,
				paxgWinTrain: paxgWinTrainA,
				solAll6h: solAll6hA,
				solAll1m: solAll1mA,
				fngHistory: fngHistory,
				dxySeries: dxyHistory,
				extraDaily: null,
				nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			Assert.NotEmpty (rowsAAll);

			var minDate = rowsAAll.First ().Date;
			var maxDate = rowsAAll.Last ().Date;

			// Берём trainUntil как 60% диапазона дат, чтобы был длинный хвост для мутации.
			var cutoffTicks = minDate.Ticks + (long) ((maxDate.Ticks - minDate.Ticks) * 0.6);
			var trainUntil = new DateTime (cutoffTicks, DateTimeKind.Utc);

			// Мутируем только будущий хвост свечей B после trainUntil.

			// Сдвигаем точку начала мутации далеко после trainUntil,
			// чтобы ни одна train-строка не использовала замутированный хвост.
			var tailStartUtc = trainUntil.AddDays (5);

			MutateFutureTail (
				solWinTrainB,
				btcWinTrainB,
				paxgWinTrainB,
				solAll6hB,
				solAll1mB,
				tailStartUtc: tailStartUtc);

			var rowsBAll = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrainB,
				btcWinTrain: btcWinTrainB,
				paxgWinTrain: paxgWinTrainB,
				solAll6h: solAll6hB,
				solAll1m: solAll1mB,
				fngHistory: fngHistory,
				dxySeries: dxyHistory,
				extraDaily: null,
				nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			// Для датасетов используем только префикс до trainUntil,
			// как и в реальном коде.
			var allRowsA = rowsAAll
				.Where (r => r.Date <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			var allRowsB = rowsBAll
				.Where (r => r.Date <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			Assert.NotEmpty (allRowsA);
			Assert.Equal (allRowsA.Count, allRowsB.Count);

			// 3. DailyDatasetBuilder + ModelTrainer: future-blind на уровне модели.

			var dsA = DailyDatasetBuilder.Build (
				allRows: allRowsA,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			var dsB = DailyDatasetBuilder.Build (
				allRows: allRowsB,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			// На уровне train-наборов строки должны совпасть.
			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MoveTrainRows, dsB.MoveTrainRows);
			AssertRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertRowsEqual (dsA.DirDownRows, dsB.DirDownRows);

			// Обучаем дневные модели A/B.
			var trainerA = new ModelTrainer ();
			var bundleA = trainerA.TrainAll (new List<DataRow> (dsA.TrainRows));

			var trainerB = new ModelTrainer ();
			var bundleB = trainerB.TrainAll (new List<DataRow> (dsB.TrainRows));

			// move
			var movePredsA = GetMovePredictions (bundleA, dsA.MoveTrainRows);
			var movePredsB = GetMovePredictions (bundleB, dsB.MoveTrainRows);
			AssertBinaryOutputsEqual (movePredsA, movePredsB);

			// dir-normal
			var dirNormalPredsA = GetDirPredictions (bundleA, dsA.DirNormalRows);
			var dirNormalPredsB = GetDirPredictions (bundleB, dsB.DirNormalRows);
			AssertBinaryOutputsEqual (dirNormalPredsA, dirNormalPredsB);

			// dir-down
			var dirDownPredsA = GetDirPredictions (bundleA, dsA.DirDownRows);
			var dirDownPredsB = GetDirPredictions (bundleB, dsB.DirDownRows);
			AssertBinaryOutputsEqual (dirDownPredsA, dirDownPredsB);

			// 4. SL-датасет + SlFirstTrainer: future-blind на SL-слое.
			const double TpPct = 0.01;   // 1%
			const double SlPct = 0.02;   // 2%

			var slDsA = SlDatasetBuilder.Build (
				rows: allRowsA,
				sol1h: null,
				sol1m: solAll1mA,
				sol6hDict: sol6hDictA,
				trainUntil: trainUntil,
				tpPct: TpPct,
				slPct: SlPct,
				strongSelector: null
			);

			var slDsB = SlDatasetBuilder.Build (
				rows: allRowsB,
				sol1h: null,
				sol1m: solAll1mB,
				sol6hDict: sol6hDictA,
				trainUntil: trainUntil,
				tpPct: TpPct,
				slPct: SlPct,
				strongSelector: null
			);

			AssertRowsEqual (slDsA.MorningRows, slDsB.MorningRows);
			Assert.True (slDsA.Samples.Count > 0, "E2E SL leakage test: synthetic SL dataset is empty.");
			Assert.Equal (slDsA.Samples.Count, slDsB.Samples.Count);

			var slTrainerA = new SlFirstTrainer ();
			var slModelA = slTrainerA.Train (slDsA.Samples, asOfUtc: trainUntil);

			var slTrainerB = new SlFirstTrainer ();
			var slModelB = new SlFirstTrainer ().Train (slDsB.Samples, asOfUtc: trainUntil);

			var slPredsA = GetSlPredictions (slModelA, slDsA.Samples);
			var slPredsB = GetSlPredictions (slModelB, slDsB.Samples);

			AssertBinaryOutputsEqual (slPredsA, slPredsB);
			}

		// === Daily predictions helpers ===

		private static List<BinaryOutput> GetMovePredictions (
			ModelBundle bundle,
			List<DataRow> rows )
			{
			if (bundle.MoveModel == null || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Label != 1,
					Features = MlTrainingUtils.ToFloatFixed (r.Features)
					}));

			var scored = bundle.MoveModel.Transform (data);

			return ml.Data
				.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false)
				.ToList ();
			}

		private static List<BinaryOutput> GetDirPredictions (
			ModelBundle bundle,
			List<DataRow> rows )
			{
			if ((bundle.DirModelNormal == null && bundle.DirModelDown == null) || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Label == 2,
					Features = MlTrainingUtils.ToFloatFixed (r.Features)
					}));

			ITransformer? model;

			if (rows.Any (r => r.RegimeDown))
				model = bundle.DirModelDown;
			else
				model = bundle.DirModelNormal;

			if (model == null)
				return new List<BinaryOutput> ();

			var scored = model.Transform (data);

			return ml.Data
				.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false)
				.ToList ();
			}

		// === SL predictions helpers ===

		private static List<BinaryOutput> GetSlPredictions (
			ITransformer model,
			List<SlHitSample> samples )
			{
			var ml = new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				samples.Select (s => new SlEvalRow
					{
					Label = s.Label,
					Features = s.Features ?? Array.Empty<float> ()
					}));

			var scored = model.Transform (data);

			return ml.Data
				.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false)
				.ToList ();
			}

		private static void AssertBinaryOutputsEqual (
			IReadOnlyList<BinaryOutput> a,
			IReadOnlyList<BinaryOutput> b,
			double tol = 1e-6 )
			{
			Assert.Equal (a.Count, b.Count);

			for (int i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.InRange (Math.Abs (a[i].Score - b[i].Score), 0.0, tol);
				Assert.InRange (Math.Abs (a[i].Probability - b[i].Probability), 0.0, tol);
				}
			}

		private static void AssertRowsEqual ( List<DataRow> xs, List<DataRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.Date, b.Date);
				Assert.Equal (a.Label, b.Label);
				Assert.Equal (a.IsMorning, b.IsMorning);
				Assert.Equal (a.MinMove, b.MinMove);
				Assert.Equal (a.RegimeDown, b.RegimeDown);

				var fa = a.Features ?? Array.Empty<double> ();
				var fb = b.Features ?? Array.Empty<double> ();

				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}

		// === synthetic history helpers ===

		private static void BuildSyntheticHistory (
			int days,
			out List<Candle6h> solWinTrain,
			out List<Candle6h> btcWinTrain,
			out List<Candle6h> paxgWinTrain,
			out List<Candle6h> solAll6h,
			out List<Candle1m> solAll1m,
			out Dictionary<DateTime, Candle6h> sol6hDict,
			out Dictionary<DateTime, double> fngHistory,
			out Dictionary<DateTime, double> dxyHistory )
			{
			var sol6 = new List<Candle6h> ();
			var btc6 = new List<Candle6h> ();
			var paxg6 = new List<Candle6h> ();
			var all1m = new List<Candle1m> ();
			var dict6 = new Dictionary<DateTime, Candle6h> ();

			fngHistory = new Dictionary<DateTime, double> ();
			dxyHistory = new Dictionary<DateTime, double> ();

			var start = new DateTime (2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var t = start;

			for (int d = 0; d < days; d++)
				{
				var day = t.Date;

				fngHistory[day] = 50.0 + (d % 10);
				dxyHistory[day] = 95.0 + (d % 5);

				for (int k = 0; k < 4; k++)
					{
					double basePrice = 120.0 + d * 0.4 + k;

					var cSol = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice,
						High = basePrice * 1.01,
						Low = basePrice * 0.99,
						Close = basePrice * 1.005
						};

					var cBtc = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 8,
						High = basePrice * 8 * 1.01,
						Low = basePrice * 8 * 0.99,
						Close = basePrice * 8 * 1.005
						};

					var cPaxg = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 0.25,
						High = basePrice * 0.25 * 1.01,
						Low = basePrice * 0.25 * 0.99,
						Close = basePrice * 0.25 * 1.005
						};

					sol6.Add (cSol);
					btc6.Add (cBtc);
					paxg6.Add (cPaxg);
					dict6[cSol.OpenTimeUtc] = cSol;

					var minuteStart = t;
					for (int m = 0; m < 360; m++)
						{
						var tm = minuteStart.AddMinutes (m);
						double p = basePrice + Math.Sin ((d * 4 + k) * 0.07 + m * 0.01) * 0.4;

						all1m.Add (new Candle1m
							{
							OpenTimeUtc = tm,
							Open = p,
							High = p * 1.0006,
							Low = p * 0.9994,
							Close = p
							});
						}

					t = t.AddHours (6);
					}
				}

			solWinTrain = sol6;
			btcWinTrain = btc6;
			paxgWinTrain = paxg6;
			solAll6h = sol6;
			solAll1m = all1m;
			sol6hDict = dict6;
			}

		private static void CloneHistory (
			List<Candle6h> solWinTrainA,
			List<Candle6h> btcWinTrainA,
			List<Candle6h> paxgWinTrainA,
			List<Candle6h> solAll6hA,
			List<Candle1m> solAll1mA,
			out List<Candle6h> solWinTrainB,
			out List<Candle6h> btcWinTrainB,
			out List<Candle6h> paxgWinTrainB,
			out List<Candle6h> solAll6hB,
			out List<Candle1m> solAll1mB )
			{
			Candle6h Clone6 ( Candle6h c ) => new Candle6h
				{
				OpenTimeUtc = c.OpenTimeUtc,
				Open = c.Open,
				High = c.High,
				Low = c.Low,
				Close = c.Close
				};

			Candle1m Clone1 ( Candle1m c ) => new Candle1m
				{
				OpenTimeUtc = c.OpenTimeUtc,
				Open = c.Open,
				High = c.High,
				Low = c.Low,
				Close = c.Close
				};

			solWinTrainB = solWinTrainA.Select (Clone6).ToList ();
			btcWinTrainB = btcWinTrainA.Select (Clone6).ToList ();
			paxgWinTrainB = paxgWinTrainA.Select (Clone6).ToList ();
			solAll6hB = solAll6hA.Select (Clone6).ToList ();
			solAll1mB = solAll1mA.Select (Clone1).ToList ();
			}

		private static void MutateFutureTail (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			List<Candle1m> solAll1m,
			DateTime tailStartUtc )
			{
			void Mutate6 ( List<Candle6h> xs, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > tailStartUtc))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			void Mutate1 ( List<Candle1m> xs, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > tailStartUtc))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			Mutate6 (solWinTrain, 1.4);
			Mutate6 (btcWinTrain, 0.75);
			Mutate6 (paxgWinTrain, 1.25);
			Mutate6 (solAll6h, 1.3);
			Mutate1 (solAll1m, 0.85);
			}
		}
	}
