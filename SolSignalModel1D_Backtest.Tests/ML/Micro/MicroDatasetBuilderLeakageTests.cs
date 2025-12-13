using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Micro
	{
	/// <summary>
	/// Инварианты микро-датасета:
	/// 1) train/micro-выборки не должны включать даты позже trainUntil;
	/// 2) MicroRows должна быть подмножеством TrainRows (иначе микро-слой может получить "будущее" относительно трейна).
	/// </summary>
	public sealed class MicroDatasetBuilderLeakageTests
		{
		[Fact]
		public void Build_UsesOnlyRowsUpToTrainUntil ()
			{
			var start = new DateTime (2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var rows = new List<DataRow> ();

			for (int i = 0; i < 50; i++)
				{
				var date = start.AddDays (i);

				rows.Add (new DataRow
					{
					Date = date,
					Features = new double[2],
					FactMicroUp = i % 2 == 0,
					FactMicroDown = i % 2 == 1
					});
				}

			var trainUntil = start.AddDays (30);

			// Позиционный вызов: переименование trainUntil* в Core не ломает тест.
			var ds = MicroDatasetBuilder.Build (rows, trainUntil);

			Assert.NotEmpty (ds.TrainRows);
			Assert.NotEmpty (ds.MicroRows);

			Assert.All (ds.TrainRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (ds.MicroRows, r => Assert.True (r.Date <= trainUntil));
			}

		[Fact]
		public void Build_MicroRowsAreSubsetOfTrainRows ()
			{
			var start = new DateTime (2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var rows = new List<DataRow> ();

			for (int i = 0; i < 50; i++)
				{
				var date = start.AddDays (i);

				rows.Add (new DataRow
					{
					Date = date,
					Features = new double[2],
					FactMicroUp = i % 3 == 0,
					FactMicroDown = i % 3 == 1
					});
				}

			var trainUntil = start.AddDays (40);

			// Позиционный вызов: переименование trainUntil* в Core не ломает тест.
			var ds = MicroDatasetBuilder.Build (rows, trainUntil);

			var trainDates = ds.TrainRows.Select (r => r.Date).ToHashSet ();

			foreach (var microRow in ds.MicroRows)
				Assert.Contains (microRow.Date, trainDates);
			}
		}
	}
