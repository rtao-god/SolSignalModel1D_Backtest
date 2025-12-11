using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Infra;
using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Тест границы trainUntil относительно baseline-exit:
	/// для всех train-дней, у которых baseline exit определён,
	/// момент выхода не должен выходить за trainUntil.
	/// </summary>
	public sealed class LeakageDailyTrainExitBoundaryTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void DailyDataset_TrainRows_DoNotCross_BaselineExit_Over_TrainUntil ()
			{
			// 1. Синтетическая история DataRow.
			var allRows = BuildSyntheticRows (count: 400);

			const int HoldoutDays = 120;
			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-HoldoutDays);

			var dataset = DailyDatasetBuilder.Build (
				allRows: allRows,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null
			);

			// 2. Собираем только те (entry, exit), где baseline-exit вообще определён.
			var entriesWithExit = new List<(DateTime EntryUtc, DateTime ExitUtc)> ();

			foreach (var r in dataset.TrainRows)
				{
				if (r.Date > trainUntil)
					continue;

				try
					{
					var exitUtc = Windowing.ComputeBaselineExitUtc (r.Date, NyTz);
					entriesWithExit.Add ((r.Date, exitUtc));
					}
				catch (InvalidOperationException)
					{
					// Weekend entry или другая ситуация, когда baseline-exit не определён — игнорируем.
					}
				}

			Assert.NotEmpty (entriesWithExit);

			// 3. Проверяем, что baseline-exit не уходит за trainUntil.
			foreach (var pair in entriesWithExit)
				{
				Assert.True (
					pair.ExitUtc <= trainUntil,
					$"entry={pair.EntryUtc:O}, exit={pair.ExitUtc:O} crosses trainUntil={trainUntil:O}"
				);
				}
			}

		/// <summary>
		/// Простая синтетика с циклом label'ов и детерминированными фичами.
		/// Здесь важен только порядок дат и стабильность Features.
		/// </summary>
		private static List<DataRow> BuildSyntheticRows ( int count )
			{
			var rows = new List<DataRow> (count);
			var start = new DateTime (2021, 10, 1, 8, 0, 0, DateTimeKind.Utc);

			for (var i = 0; i < count; i++)
				{
				var date = start.AddDays (i);
				var label = i % 3;

				var features = new[]
				{
					i / (double)count,
					Math.Sin (i * 0.05),
					Math.Cos (i * 0.07),
					label
				};

				rows.Add (new DataRow
					{
					Date = date,
					Label = label,
					RegimeDown = (i % 5 == 0),
					Features = features
					});
				}

			return rows
				.OrderBy (r => r.Date)
				.ToList ();
			}
		}
	}
