using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Micro
	{
	public class LeakageMicroModelTrainingTests
		{
		private sealed class BinaryOutput
			{
			public bool PredictedLabel { get; set; }
			public float Score { get; set; }
			public float Probability { get; set; }
			}

		[Fact]
		public void MicroModel_Training_IsFutureBlind_ToOosTailMutation_ByTrainBoundary ()
			{
			var nyTz = Windowing.NyTz;

			var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc (
				startNyLocalDate: NyTestDates.NyLocal (2024, 1, 2, 0),
				count: 260,
				hour: 8);

			var allRows = BuildSyntheticRows (datesUtc);

			// trainUntil берём как baseline-exit близко к концу ряда.
			var pivotEntry = datesUtc[^40];
			var pivotExit = Windowing.ComputeBaselineExitUtc (pivotEntry, nyTz);
			var trainUntilUtc = pivotExit.AddMinutes (1);

			var boundary = new TrainBoundary (trainUntilUtc, nyTz);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateOosTail (rowsB, boundary);

			var dsA = MicroDatasetBuilder.Build (rowsA, trainUntilUtc);
			var dsB = MicroDatasetBuilder.Build (rowsB, trainUntilUtc);

			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MicroRows, dsB.MicroRows);

			if (dsA.MicroRows.Count < 50)
				throw new InvalidOperationException (
					$"LeakageMicroModelTrainingTests: micro dataset too small ({dsA.MicroRows.Count}).");

			var mlA = new MLContext (seed: 42);
			// ВАЖНО: тренер ожидает List<DataRow>, а датасет отдаёт IReadOnlyList<DataRow>.
			// Приводим явно, чтобы не расширять контракт Core ради тестов.
			var modelA = MicroFlatTrainer.BuildMicroFlatModel (mlA, new List<DataRow> (dsA.TrainRows));
			Assert.NotNull (modelA);

			var mlB = new MLContext (seed: 42);
			var modelB = MicroFlatTrainer.BuildMicroFlatModel (mlB, new List<DataRow> (dsB.TrainRows));
			Assert.NotNull (modelB);

			var predsA = GetMicroPredictions (mlA, modelA!, dsA.MicroRows.ToList ());
			var predsB = GetMicroPredictions (mlB, modelB!, dsB.MicroRows.ToList ());

			AssertBinaryOutputsEqual (predsA, predsB);
			}

		private static List<DataRow> BuildSyntheticRows ( IReadOnlyList<DateTime> datesUtc )
			{
			var rows = new List<DataRow> (datesUtc.Count);

			for (int i = 0; i < datesUtc.Count; i++)
				{
				var isMicro = (i % 3 == 0);
				var up = isMicro && (i % 6 == 0);

				rows.Add (new DataRow
					{
					Date = datesUtc[i],
					Features = new[]
					{
						i / (double)datesUtc.Count,
						Math.Sin(i * 0.03),
						Math.Cos(i * 0.05),
						isMicro ? 1.0 : 0.0
					},
					FactMicroUp = up,
					FactMicroDown = isMicro && !up
					});
				}

			return rows;
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

		private static void MutateOosTail ( List<DataRow> rows, TrainBoundary boundary )
			{
			// Мутируем всё, что boundary НЕ считает train:
			// цель — доказать, что обучение/датасет не зависит от OOS “будущего”.
			foreach (var r in rows)
				{
				if (boundary.IsTrainEntry (r.Date))
					continue;

				r.FactMicroUp = !r.FactMicroUp;
				r.FactMicroDown = !r.FactMicroDown;

				if (r.Features is { Length: > 0 })
					{
					for (int i = 0; i < r.Features.Length; i++)
						r.Features[i] = 9999.0 + i;
					}
				}
			}

		private static void AssertRowsEqual ( IReadOnlyList<DataRow> xs, IReadOnlyList<DataRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.Date, b.Date);
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				var fa = a.Features ?? Array.Empty<double> ();
				var fb = b.Features ?? Array.Empty<double> ();

				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
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

			for (int i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.InRange (Math.Abs (a[i].Score - b[i].Score), 0.0, tol);
				Assert.InRange (Math.Abs (a[i].Probability - b[i].Probability), 0.0, tol);
				}
			}
		}
	}
