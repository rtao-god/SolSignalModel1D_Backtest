using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Тесты утечки на уровне обучения дневной модели (move + dir).
	/// Предполагается, что DailyDatasetBuilder уже прошёл свои future-blind тесты.
	/// Здесь проверяется:
	///   - изменение хвоста (Date > trainUntilUtc) не меняет предсказания на train-наборе.
	/// </summary>
	public sealed class LeakageDailyModelTrainingTests
		{
		private sealed class BinaryOutput
			{
			// Имена совпадают с дефолтными колонками ML.NET LightGBM
			public bool PredictedLabel { get; set; }
			public float Score { get; set; }
			public float Probability { get; set; }
			}

		[Fact]
		public void DailyMoveAndDir_Training_IsFutureBlind_ToTailMutation ()
			{
			// 1. Строим синтетическую историю BacktestRecord.
			var allRows = BuildSyntheticRows (count: 400);

			const int HoldoutDays = 120;
			var maxDate = allRows.Last ().Date;
			var trainUntilUtc = maxDate.AddDays (-HoldoutDays);

			Assert.Contains (allRows, r => r.Causal.DateUtc > trainUntilUtc);

			// 2. Две копии: A — оригинал, B — с жёстко замутивированным хвостом.
			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntilUtc);

			// 3. Датасеты A и B (они уже future-blind, то есть train-часть совпадает).
			var dsA = DailyDatasetBuilder.Build (
				allRows: rowsA,
				trainUntilUtc: trainUntilUtc,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			var dsB = DailyDatasetBuilder.Build (
				allRows: rowsB,
				trainUntilUtc: trainUntilUtc,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			// sanity: train-часть у датасетов совпадает
			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MoveTrainRows, dsB.MoveTrainRows);
			AssertRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertRowsEqual (dsA.DirDownRows, dsB.DirDownRows);

			// 4. Обучаем дневные модели на train-части A и B.
			var trainerA = new ModelTrainer ();
			var bundleA = trainerA.TrainAll (dsA.TrainRows);

			var trainerB = new ModelTrainer ();
			var bundleB = trainerB.TrainAll (dsB.TrainRows);

			// 5. Сравниваем предсказания move/dir на одних и тех же train-данных.
			var movePredsA = GetMovePredictions (bundleA, dsA.MoveTrainRows);
			var movePredsB = GetMovePredictions (bundleB, dsB.MoveTrainRows);
			AssertBinaryOutputsEqual (movePredsA, movePredsB);

			var dirNormalPredsA = GetDirPredictions (bundleA, dsA.DirNormalRows);
			var dirNormalPredsB = GetDirPredictions (bundleB, dsB.DirNormalRows);
			AssertBinaryOutputsEqual (dirNormalPredsA, dirNormalPredsB);

			var dirDownPredsA = GetDirPredictions (bundleA, dsA.DirDownRows);
			var dirDownPredsB = GetDirPredictions (bundleB, dsB.DirDownRows);
			AssertBinaryOutputsEqual (dirDownPredsA, dirDownPredsB);
			}

		// === вспомогательные методы ===

		private static List<BacktestRecord> BuildSyntheticRows ( int count )
			{
			var rows = new List<BacktestRecord> (count);
			var start = new DateTime (2021, 10, 1, 8, 0, 0, DateTimeKind.Utc);

			for (var i = 0; i < count; i++)
				{
				var date = start.AddDays (i);

				// простая схема: 0 / 1 / 2 по кругу
				var label = i % 3;

				var features = new[]
				{
					i / (double) count,
					Math.Sin (i * 0.05),
					Math.Cos (i * 0.07),
					label
				};

				rows.Add (new BacktestRecord
					{
					Date = date,
					Label = label,
					RegimeDown = (i % 5 == 0),
					Features = features
					});
				}

			return rows.OrderBy (r => r.Causal.DateUtc).ToList ();
			}

		private static List<BacktestRecord> CloneRows ( List<BacktestRecord> src )
			{
			var res = new List<BacktestRecord> (src.Count);

			foreach (var r in src)
				{
				res.Add (new BacktestRecord
					{
					Date = r.Causal.DateUtc,
					Label = r.Forward.TrueLabel,
					RegimeDown = r.RegimeDown,
					Features = r.Causal.Features?.ToArray () ?? Array.Empty<double> ()
					});
				}

			return res;
			}

		/// <summary>
		/// Мутируем только хвост Date &gt; trainUntilUtc, имитируя "инородное будущее".
		/// </summary>
		private static void MutateFutureTail ( List<BacktestRecord> rows, DateTime trainUntilUtc )
			{
			foreach (var r in rows.Where (r => r.Causal.DateUtc > trainUntilUtc))
				{
				r.Forward.TrueLabel = 2;
				r.RegimeDown = !r.RegimeDown;

				if (r.Causal.Features is { Length: > 0 })
					{
					for (var i = 0; i < r.Causal.Features.Length; i++)
						r.Causal.Features[i] = 10_000.0 + i;
					}
				}
			}

		private static void AssertRowsEqual ( IReadOnlyList<BacktestRecord> xs, IReadOnlyList<BacktestRecord> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (var i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.Causal.DateUtc, b.Causal.DateUtc);
				Assert.Equal (a.Forward.TrueLabel, b.Forward.TrueLabel);
				Assert.Equal (a.RegimeDown, b.RegimeDown);

				var fa = a.Causal.Features ?? Array.Empty<double> ();
				var fb = b.Causal.Features ?? Array.Empty<double> ();

				Assert.Equal (fa.Length, fb.Length);
				for (var j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}

		private static List<BinaryOutput> GetMovePredictions ( ModelBundle bundle, IReadOnlyList<BacktestRecord> rows )
			{
			if (bundle.MoveModel == null || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Forward.TrueLabel != 1,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.Features)
					}));

			var scored = bundle.MoveModel.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static List<BinaryOutput> GetDirPredictions ( ModelBundle bundle, IReadOnlyList<BacktestRecord> rows )
			{
			if ((bundle.DirModelNormal == null && bundle.DirModelDown == null) || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Forward.TrueLabel == 2, // up = true, down = false
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.Features)
					}));

			// Выбор модели зависит от поднабора, который подаётся в тест (normal vs down).
			ITransformer? model = rows.Any (r => r.RegimeDown) ? bundle.DirModelDown : bundle.DirModelNormal;
			if (model == null)
				return new List<BinaryOutput> ();

			var scored = model.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static void AssertBinaryOutputsEqual ( IReadOnlyList<BinaryOutput> a, IReadOnlyList<BinaryOutput> b )
			{
			Assert.Equal (a.Count, b.Count);

			for (int i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.Equal (a[i].Score, b[i].Score, 6);
				Assert.Equal (a[i].Probability, b[i].Probability, 6);
				}
			}
		}
	}
