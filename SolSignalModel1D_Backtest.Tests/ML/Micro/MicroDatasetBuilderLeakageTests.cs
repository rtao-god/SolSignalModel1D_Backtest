using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Micro
	{
	/// <summary>
	/// Тесты для MicroDatasetBuilder:
	/// - проверка, что train/micro-датасеты режутся по trainUntil;
	/// - проверка, что MicroRows — подмножество TrainRows.
	/// </summary>
	public sealed class MicroDatasetBuilderLeakageTests
		{
		/// <summary>
		/// Сценарий:
		/// - строим последовательность дней до и после trainUntil;
		/// - размечаем FactMicroUp / FactMicroDown как угодно;
		/// - убеждаемся, что в MicroRows/TrainRows нет дат > trainUntil.
		/// </summary>
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
					// Простая разметка: через один up/down.
					FactMicroUp = i % 2 == 0,
					FactMicroDown = i % 2 == 1
					});
				}

			var trainUntil = start.AddDays (30);

			var ds = MicroDatasetBuilder.Build (
				allRows: rows,
				trainUntil: trainUntil);

			Assert.NotEmpty (ds.TrainRows);
			Assert.NotEmpty (ds.MicroRows);

			Assert.All (ds.TrainRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (ds.MicroRows, r => Assert.True (r.Date <= trainUntil));
			}

		/// <summary>
		/// MicroRows должен быть подмножеством TrainRows:
		/// если это когда-нибудь перестанет быть так, микро-слой будет иметь шанс "увидеть будущее".
		/// </summary>
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

			var ds = MicroDatasetBuilder.Build (
				allRows: rows,
				trainUntil: trainUntil);

			var trainDates = ds.TrainRows
				.Select (r => r.Date)
				.ToHashSet ();

			foreach (var microRow in ds.MicroRows)
				{
				Assert.Contains (microRow.Date, trainDates);
				}
			}
		}
	}
