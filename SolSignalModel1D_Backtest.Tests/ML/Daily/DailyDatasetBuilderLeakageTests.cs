using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Daily
	{
	public sealed class DailyDatasetBuilderLeakageTests
		{
		[Fact]
		public void Build_CutsTrainRowsByBaselineExit_NotByEntryUtc ()
			{
			var nyTz = Windowing.NyTz;

			var entryUtc = NyTestDates.ToUtc (NyTestDates.NyLocal (2025, 1, 6, 8, 0)); // понедельник
			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			Assert.True (exitUtc > entryUtc);

			var rows = new List<BacktestRecord>
			{
				CreateRow(dateUtc: entryUtc, label: 2, regimeDown: false)
			};

			// trainUntil между entry и exit => baseline-окно пересекает "будущее" => row обязана быть выкинута из train.
			var midTicks = entryUtc.Ticks + (exitUtc.Ticks - entryUtc.Ticks) / 2;
			var trainUntilBetween = new DateTime (midTicks, DateTimeKind.Utc);

			// Позиционный вызов: переименование trainUntil* в Core не ломает тест.
			var dsBeforeExit = DailyDatasetBuilder.Build (rows, trainUntilBetween, false, false, 0.5, null);
			Assert.Empty (dsBeforeExit.TrainRows);

			// trainUntil после exit => row должна попасть.
			var trainUntilAfterExit = exitUtc.AddMinutes (1);
			var dsAfterExit = DailyDatasetBuilder.Build (rows, trainUntilAfterExit, false, false, 0.5, null);

			Assert.Single (dsAfterExit.TrainRows);
			Assert.Equal (entryUtc, dsAfterExit.TrainRows[0].Date);
			}

		[Fact]
		public void Build_AllTrainListsContainOnlyTrainEntries_ByTrainBoundary ()
			{
			var nyTz = Windowing.NyTz;

			var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc (
				startNyLocalDate: NyTestDates.NyLocal (2025, 1, 1, 0),
				count: 120,
				hour: 8);

			var rows = new List<BacktestRecord> (datesUtc.Count);
			for (int i = 0; i < datesUtc.Count; i++)
				{
				rows.Add (CreateRow (
					dateUtc: datesUtc[i],
					label: i % 3,
					regimeDown: (i % 5 == 0)));
				}

			// trainUntil задаём как baseline-exit одной из поздних дат, чтобы граница была "реалистичной" (как в проде).
			var pivotEntry = datesUtc[^20];
			var pivotExit = Windowing.ComputeBaselineExitUtc (pivotEntry, nyTz);
			var trainUntilUtc = pivotExit.AddMinutes (1);

			var boundary = new TrainBoundary (trainUntilUtc, nyTz);

			// Позиционный вызов: переименование trainUntil* в Core не ломает тест.
			var ds = DailyDatasetBuilder.Build (rows, trainUntilUtc, false, false, 0.5, null);

			Assert.NotEmpty (ds.TrainRows);

			static void AssertAllTrain ( IEnumerable<BacktestRecord> xs, TrainBoundary b, string tag )
				{
				foreach (var r in xs)
					{
					Assert.True (
						b.IsTrainEntry (r.ToCausalDateUtc()),
						$"{tag} contains non-train entry by TrainBoundary: {r.ToCausalDateUtc():O}");
					}
				}

			AssertAllTrain (ds.TrainRows, boundary, nameof (ds.TrainRows));
			AssertAllTrain (ds.MoveTrainRows, boundary, nameof (ds.MoveTrainRows));
			AssertAllTrain (ds.DirNormalRows, boundary, nameof (ds.DirNormalRows));
			AssertAllTrain (ds.DirDownRows, boundary, nameof (ds.DirDownRows));
			}

		private static BacktestRecord CreateRow ( DateTime dateUtc, int label, bool regimeDown )
			{
			return new BacktestRecord
				{
				Date = dateUtc,
				Label = label,
				RegimeDown = regimeDown,
				IsMorning = true,
				MinMove = 0.02,
				Features = new double[4]
				};
			}
		}
	}
