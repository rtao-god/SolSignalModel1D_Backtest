using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Micro
	{
	/// <summary>
	/// Тест: MicroDatasetBuilder не использует строки с Date > trainUntil
	/// и future-blind к мутациям хвоста.
	/// </summary>
	public class LeakageMicroDatasetTests
		{
		[Fact]
		public void MicroDataset_UsesOnlyRows_UntilTrainUntil_AndIsFutureBlind ()
			{
			var allRows = BuildSyntheticRows (200);

			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-40);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntil);

			var dsA = MicroDatasetBuilder.Build (rowsA, trainUntil);
			var dsB = MicroDatasetBuilder.Build (rowsB, trainUntil);

			Assert.All (dsA.TrainRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsB.TrainRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsA.MicroRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsB.MicroRows, r => Assert.True (r.Date <= trainUntil));

			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MicroRows, dsB.MicroRows);
			}

		private static List<DataRow> BuildSyntheticRows ( int count )
			{
			var rows = new List<DataRow> (count);
			var start = new DateTime (2022, 3, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var date = start.AddDays (i);

				bool isMicro = (i % 4 == 0);
				bool up = (i % 8 == 0);

				var row = new DataRow
					{
					Date = date,
					Features = new[] { 0.1 * i, i, isMicro ? 1.0 : 0.0 },
					FactMicroUp = isMicro && up,
					FactMicroDown = isMicro && !up
					};

				rows.Add (row);
				}

			return rows
				.OrderBy (r => r.Date)
				.ToList ();
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
				// инвертируем микро-разметку и сильно меняем фичи
				bool wasUp = r.FactMicroUp;
				bool wasDown = r.FactMicroDown;

				r.FactMicroUp = !wasUp;
				r.FactMicroDown = !wasDown;

				if (r.Features != null && r.Features.Length > 0)
					{
					for (int i = 0; i < r.Features.Length; i++)
						r.Features[i] = 42.0 + i;
					}
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
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				var fa = a.Features ?? Array.Empty<double> ();
				var fb = b.Features ?? Array.Empty<double> ();
				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}
		}
	}
