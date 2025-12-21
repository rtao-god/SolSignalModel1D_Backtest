using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
	{
	public sealed class DailyDataset
		{
		public List<LabeledCausalRow> TrainRows { get; }
		public List<LabeledCausalRow> MoveTrainRows { get; }
		public List<LabeledCausalRow> DirNormalRows { get; }
		public List<LabeledCausalRow> DirDownRows { get; }
		public DateTime TrainUntilUtc { get; }

		public DailyDataset (
			List<LabeledCausalRow> trainRows,
			List<LabeledCausalRow> moveTrainRows,
			List<LabeledCausalRow> dirNormalRows,
			List<LabeledCausalRow> dirDownRows,
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
			List<LabeledCausalRow> allRows,
			DateTime trainUntil,
			bool balanceMove,
			bool balanceDir,
			double balanceTargetFrac,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			// Убираем вызовы r.ToCausalDateUtc() (ambiguous extension).
			static DateTime EntryUtc ( LabeledCausalRow r ) => r.Causal.DateUtc;
			static DateTime DayKeyUtc ( LabeledCausalRow r ) => r.Causal.DateUtc.ToCausalDateUtc ();

			var ordered = allRows
				.OrderBy (EntryUtc)
				.ToList ();

			var trainRows = ordered
				.Where (r => EntryUtc (r) <= trainUntil)
				.ToList ();

			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				// datesToExclude обычно хранит day-key (00:00 UTC), поэтому сравниваем по DayKeyUtc.
				trainRows = trainRows
					.Where (r => !datesToExclude.Contains (DayKeyUtc (r)))
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

		private static List<LabeledCausalRow> FilterByBaselineExit ( List<LabeledCausalRow> rows, DateTime trainUntil )
			{
			static DateTime EntryUtc ( LabeledCausalRow r ) => r.Causal.DateUtc;

			var result = new List<LabeledCausalRow> (rows.Count);

			foreach (var r in rows)
				{
				var entryUtc = EntryUtc (r);

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
