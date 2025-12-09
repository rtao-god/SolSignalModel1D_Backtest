using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Тест: DailyDatasetBuilder не использует хвост (Date > trainUntil) и future-blind.
	/// </summary>
	public class LeakageDailyDatasetTests
		{
		[Fact]
		public void DailyDataset_UsesOnlyRows_UntilTrainUntil_AndIsFutureBlind ()
			{
			var allRows = BuildSyntheticRows (count: 300);

			const int HoldoutDays = 120;
			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-HoldoutDays);

			Assert.Contains (allRows, r => r.Date > trainUntil);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntil);

			var datasetA = DailyDatasetBuilder.Build (
				allRows: rowsA,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			var datasetB = DailyDatasetBuilder.Build (
				allRows: rowsB,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			AssertAllDatesNotAfter (datasetA, trainUntil);
			AssertAllDatesNotAfter (datasetB, trainUntil);

			AssertRowsEqual (datasetA.TrainRows, datasetB.TrainRows);
			AssertRowsEqual (datasetA.MoveTrainRows, datasetB.MoveTrainRows);
			AssertRowsEqual (datasetA.DirNormalRows, datasetB.DirNormalRows);
			AssertRowsEqual (datasetA.DirDownRows, datasetB.DirDownRows);
			}

		private static List<DataRow> BuildSyntheticRows ( int count )
			{
			var rows = new List<DataRow> (count);
			var start = new DateTime (2022, 1, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var date = start.AddDays (i);
				int label = i % 3;

				double x = i / (double) count;
				var features = new[]
				{
					x,
					Math.Sin(x * Math.PI),
					Math.Cos(x * Math.PI),
					label
				};

				var row = new DataRow
					{
					Date = date,
					Label = label,
					RegimeDown = (i % 5 == 0),
					Features = features
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
				var clone = new DataRow
					{
					Date = r.Date,
					Label = r.Label,
					RegimeDown = r.RegimeDown,
					Features = r.Features?.ToArray () ?? Array.Empty<double> ()
					};

				res.Add (clone);
				}

			return res;
			}

		private static void MutateFutureTail ( List<DataRow> rows, DateTime trainUntil )
			{
			foreach (var r in rows.Where (r => r.Date > trainUntil))
				{
				r.RegimeDown = !r.RegimeDown;
				r.Label = 2;

				if (r.Features != null && r.Features.Length > 0)
					{
					for (int i = 0; i < r.Features.Length; i++)
						{
						r.Features[i] = 1000.0 + i;
						}
					}
				}
			}

		private static void AssertAllDatesNotAfter ( DailyDataset ds, DateTime trainUntil )
			{
			Assert.All (ds.TrainRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (ds.MoveTrainRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (ds.DirNormalRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (ds.DirDownRows, r => Assert.True (r.Date <= trainUntil));
			}

		private static void AssertRowsEqual ( List<DataRow> xs, List<DataRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var r1 = xs[i];
				var r2 = ys[i];

				Assert.Equal (r1.Date, r2.Date);
				Assert.Equal (r1.Label, r2.Label);
				Assert.Equal (r1.RegimeDown, r2.RegimeDown);

				var f1 = r1.Features ?? Array.Empty<double> ();
				var f2 = r2.Features ?? Array.Empty<double> ();

				Assert.Equal (f1.Length, f2.Length);

				for (int j = 0; j < f1.Length; j++)
					{
					Assert.Equal (f1[j], f2[j]);
					}
				}
			}
		}
	}
