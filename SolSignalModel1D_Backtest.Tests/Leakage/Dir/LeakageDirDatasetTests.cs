using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Dir;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Dir
	{
	/// <summary>
	/// Тест: DirDatasetBuilder не тащит в себя дни с Date > trainUntil.
	/// По сути, это thin-обёртка над DailyDatasetBuilder, но тест фиксирует контракт.
	/// </summary>
	public class LeakageDirDatasetTests
		{
		[Fact]
		public void DirDataset_UsesOnlyRows_UntilTrainUntil ()
			{
			var allRows = BuildSyntheticRows (200);

			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-60);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntil);

			var dsA = DirDatasetBuilder.Build (
				allRows: rowsA,
				trainUntil: trainUntil,
				balanceDir: true,
				balanceTargetFrac: 0.7);

			var dsB = DirDatasetBuilder.Build (
				allRows: rowsB,
				trainUntil: trainUntil,
				balanceDir: true,
				balanceTargetFrac: 0.7);

			Assert.All (dsA.DirNormalRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsA.DirDownRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsB.DirNormalRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsB.DirDownRows, r => Assert.True (r.Date <= trainUntil));

			AssertRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertRowsEqual (dsA.DirDownRows, dsB.DirDownRows);
			}

		private static List<DataRow> BuildSyntheticRows ( int count )
			{
			var rows = new List<DataRow> (count);
			var start = new DateTime (2022, 2, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var date = start.AddDays (i);
				int label = (i % 2 == 0) ? 0 : 2; // только down/up для dir

				var row = new DataRow
					{
					Date = date,
					Label = label,
					RegimeDown = (i % 3 == 0),
					Features = new[] { 1.0, i, label }
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
					Label = r.Label,
					RegimeDown = r.RegimeDown,
					Features = r.Features?.ToArray () ?? Array.Empty<double> ()
					});
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
						r.Features[i] = -999.0 - i;
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
				Assert.Equal (a.Label, b.Label);
				Assert.Equal (a.RegimeDown, b.RegimeDown);

				var fa = a.Features ?? Array.Empty<double> ();
				var fb = b.Features ?? Array.Empty<double> ();
				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}
		}
	}
