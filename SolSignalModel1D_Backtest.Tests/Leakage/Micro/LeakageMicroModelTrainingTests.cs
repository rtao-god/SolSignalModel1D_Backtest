using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Micro
	{
	/// <summary>
	/// Тесты утечки на уровне обучения микро-модели.
	/// Предполагается, что MicroDatasetBuilder уже гарантирует future-blind.
	/// Здесь проверяем инвариант: прогнозы на микро-днях не меняются
	/// при изменении хвоста после trainUntil.
	/// </summary>
	public class LeakageMicroModelTrainingTests
		{
		private sealed class BinaryOutput
			{
			public bool PredictedLabel { get; set; }
			public float Score { get; set; }
			public float Probability { get; set; }
			}

		[Fact]
		public void MicroModel_Training_IsFutureBlind_ToTailMutation ()
			{
			// 1. Синтетические DataRow с разметкой FactMicroUp / FactMicroDown.
			var allRows = BuildSyntheticRows (250);

			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-40);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntil);

			// 2. Датасеты микро-слоя A/B.
			var dsA = MicroDatasetBuilder.Build (rowsA, trainUntil);
			var dsB = MicroDatasetBuilder.Build (rowsB, trainUntil);

			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MicroRows, dsB.MicroRows);

			if (dsA.MicroRows.Count < 50)
				{
				// защитный guard: при слишком маленьком датасете MicroFlatTrainer по
				// контракту может просто вернуть null. Тогда лучше явно упасть,
				// чем иметь "тихий" тест, ничего не проверяющий.
				throw new InvalidOperationException (
					$"LeakageMicroModelTrainingTests: synthetic micro dataset too small ({dsA.MicroRows.Count}).");
				}

			var mlA = new MLContext (seed: 42);
			var modelA = MicroFlatTrainer.BuildMicroFlatModel (mlA, dsA.TrainRows);
			Assert.NotNull (modelA);

			var mlB = new MLContext (seed: 42);
			var modelB = MicroFlatTrainer.BuildMicroFlatModel (mlB, dsB.TrainRows);
			Assert.NotNull (modelB);

			var predsA = GetMicroPredictions (mlA, modelA!, dsA.MicroRows);
			var predsB = GetMicroPredictions (mlB, modelB!, dsB.MicroRows);

			AssertBinaryOutputsEqual (predsA, predsB);
			}

		// === helpers ===

		private static List<DataRow> BuildSyntheticRows ( int count )
			{
			var rows = new List<DataRow> (count);
			var start = new DateTime (2022, 1, 1, 8, 0, 0, DateTimeKind.Utc);

			for (var i = 0; i < count; i++)
				{
				var date = start.AddDays (i);

				var isMicro = (i % 3 == 0);
				var up = isMicro && (i % 6 == 0);

				var features = new[]
				{
					i / (double) count,
					Math.Sin(i * 0.03),
					Math.Cos(i * 0.05),
					isMicro ? 1.0 : 0.0
				};

				var row = new DataRow
					{
					Date = date,
					Features = features,
					FactMicroUp = up,
					FactMicroDown = isMicro && !up
					};

				rows.Add (row);
				}

			return rows.OrderBy (r => r.Date).ToList ();
			}

		private static List<DataRow> CloneRows ( List<DataRow> src )
			{
			var res = new List<DataRow> (src.Count);

			foreach (var r in src)
				{
				res.Add (new DataRow
					{
					Date = r.Date,
					Features = r.Features?.ToArray () ?? Array.Empty<double> (),
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown
					});
				}

			return res;
			}

		private static void MutateFutureTail ( List<DataRow> rows, DateTime trainUntil )
			{
			foreach (var r in rows.Where (r => r.Date > trainUntil))
				{
				// Инвертируем микро-факт и рушим фичи.
				var wasUp = r.FactMicroUp;
				var wasDown = r.FactMicroDown;

				r.FactMicroUp = !wasUp;
				r.FactMicroDown = !wasDown;

				if (r.Features is { Length: > 0 })
					{
					for (var i = 0; i < r.Features.Length; i++)
						{
						r.Features[i] = 9999.0 + i;
						}
					}
				}
			}

		private static void AssertRowsEqual ( List<DataRow> xs, List<DataRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (var i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.Date, b.Date);
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				var fa = a.Features ?? Array.Empty<double> ();
				var fb = b.Features ?? Array.Empty<double> ();

				Assert.Equal (fa.Length, fb.Length);
				for (var j = 0; j < fa.Length; j++)
					{
					Assert.Equal (fa[j], fb[j]);
					}
				}
			}

		private static List<BinaryOutput> GetMicroPredictions (
			MLContext ml,
			ITransformer model,
			List<DataRow> rows )
			{
			if (rows.Count == 0)
				return new List<BinaryOutput> ();

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.FactMicroUp,
					Features = MlTrainingUtils.ToFloatFixed (r.Features)
					}));

			var scored = model.Transform (data);

			return ml.Data
				.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false)
				.ToList ();
			}

		private static void AssertBinaryOutputsEqual (
			List<BinaryOutput> a,
			List<BinaryOutput> b,
			double tol = 1e-6 )
			{
			Assert.Equal (a.Count, b.Count);

			for (var i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.InRange (Math.Abs (a[i].Score - b[i].Score), 0.0, tol);
				Assert.InRange (Math.Abs (a[i].Probability - b[i].Probability), 0.0, tol);
				}
			}
		}
	}
