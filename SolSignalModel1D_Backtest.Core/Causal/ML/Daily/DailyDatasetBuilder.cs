using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
	{
	public sealed class DailyDataset
		{
		public List<BacktestRecord> TrainRows { get; }
		public List<BacktestRecord> MoveTrainRows { get; }
		public List<BacktestRecord> DirNormalRows { get; }
		public List<BacktestRecord> DirDownRows { get; }
		public DateTime TrainUntilUtc { get; }

		public DailyDataset (
			List<BacktestRecord> trainRows,
			List<BacktestRecord> moveTrainRows,
			List<BacktestRecord> dirNormalRows,
			List<BacktestRecord> dirDownRows,
			DateTime trainUntilUtc )
			{
			TrainRows = trainRows ?? throw new ArgumentNullException (nameof (trainRows));
			MoveTrainRows = moveTrainRows ?? throw new ArgumentNullException (nameof (moveTrainRows));
			DirNormalRows = dirNormalRows ?? throw new ArgumentNullException (nameof (dirNormalRows));
			DirDownRows = dirDownRows ?? throw new ArgumentNullException (nameof (dirDownRows));
			TrainUntilUtc = trainUntilUtc;
			}
		}

	public static class DailyDatasetBuilder
		{
		private static readonly TimeZoneInfo NyTz = Windowing.NyTz;

		public static DailyDataset Build (
			List<BacktestRecord> allRows,
			DateTime trainUntil,
			bool balanceMove,
			bool balanceDir,
			double balanceTargetFrac,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			var ordered = allRows
				.OrderBy (r => r.ToCausalDateUtc ())
				.ToList ();

			var trainRows = ordered
				.Where (r => r.ToCausalDateUtc () <= trainUntil)
				.ToList ();

			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				trainRows = trainRows
					.Where (r => !datesToExclude.Contains (r.ToCausalDateUtc ()))
					.ToList ();
				}

			trainRows = FilterByBaselineExit (trainRows, trainUntil);

			DailyTrainingDataBuilder.Build (
				trainRows: trainRows,
				balanceMove: balanceMove,
				balanceDir: balanceDir,
				balanceTargetFrac: balanceTargetFrac,
				moveTrainRows: out var moveTrainRows,
				dirNormalRows: out var dirNormalRows,
				dirDownRows: out var dirDownRows);

			return new DailyDataset (
				trainRows: trainRows,
				moveTrainRows: moveTrainRows,
				dirNormalRows: dirNormalRows,
				dirDownRows: dirDownRows,
				trainUntilUtc: trainUntil);
			}

		private static List<BacktestRecord> FilterByBaselineExit ( List<BacktestRecord> rows, DateTime trainUntil )
			{
			var result = new List<BacktestRecord> (rows.Count);

			foreach (var r in rows)
				{
				var entryUtc = r.ToCausalDateUtc ();

				var ny = TimeZoneInfo.ConvertTimeFromUtc (entryUtc, NyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz);

				if (exitUtc <= trainUntil)
					result.Add (r);
				}

			return result;
			}
		}
	}
