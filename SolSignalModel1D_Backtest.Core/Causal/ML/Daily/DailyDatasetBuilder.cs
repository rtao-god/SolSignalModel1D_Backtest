using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
	{
	public sealed class DailyDataset
		{
		public IReadOnlyList<BacktestRecord> TrainRows { get; }
		public IReadOnlyList<BacktestRecord> MoveTrainRows { get; }
		public IReadOnlyList<BacktestRecord> DirNormalRows { get; }
		public IReadOnlyList<BacktestRecord> DirDownRows { get; }
		public DateTime TrainUntilUtc { get; }

		public DailyDataset (
			IReadOnlyList<BacktestRecord> trainRows,
			IReadOnlyList<BacktestRecord> moveTrainRows,
			IReadOnlyList<BacktestRecord> dirNormalRows,
			IReadOnlyList<BacktestRecord> dirDownRows,
			DateTime trainUntilUtc )
			{
			TrainRows = trainRows ?? throw new ArgumentNullException (nameof (trainRows));
			MoveTrainRows = moveTrainRows ?? throw new ArgumentNullException (nameof (moveTrainRows));
			DirNormalRows = dirNormalRows ?? throw new ArgumentNullException (nameof (dirNormalRows));
			DirDownRows = dirDownRows ?? throw new ArgumentNullException (nameof (dirDownRows));

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			TrainUntilUtc = trainUntilUtc;
			}
		}

	public static class DailyDatasetBuilder
		{
		private static readonly TimeZoneInfo NyTz = Windowing.NyTz;

		public static DailyDataset Build (
			IReadOnlyList<BacktestRecord> allRows,
			DateTime trainUntilUtc,
			bool balanceMove,
			bool balanceDir,
			double balanceTargetFrac,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			// Контракт: allRows уже отсортирован на бутстрапе/RowBuilder.
			SeriesGuards.EnsureStrictlyAscendingUtc (allRows, r => r.Causal.DateUtc, "daily-dataset.allRows");

			// Split требует список: не сортируем, только приводим тип при необходимости.
			var ordered = allRows as List<BacktestRecord> ?? allRows.ToList ();

			var boundary = new TrainBoundary (trainUntilUtc, NyTz);
			var split = boundary.Split (ordered, r => r.Causal.DateUtc);

			if (split.Excluded.Count > 0)
				{
				var sample = split.Excluded
					.Take (Math.Min (10, split.Excluded.Count))
					.Select (r => r.Causal.DateUtc.ToString ("O"));

				throw new InvalidOperationException (
					$"[daily-dataset] Found excluded days (baseline-exit undefined). " +
					$"count={split.Excluded.Count}. sample=[{string.Join (", ", sample)}].");
				}

			// Builder ниже ожидает List<BacktestRecord>.
			List<BacktestRecord> trainEligible = split.Train as List<BacktestRecord> ?? split.Train.ToList ();

			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				trainEligible = trainEligible
					.Where (r => !datesToExclude.Contains (r.Causal.DateUtc))
					.ToList ();
				}

			if (trainEligible.Count == 0)
				throw new InvalidOperationException ("[daily-dataset] trainEligible is empty after filters.");

			DailyTrainingDataBuilder.Build (
				trainRows: trainEligible,
				balanceMove: balanceMove,
				balanceDir: balanceDir,
				balanceTargetFrac: balanceTargetFrac,
				moveTrainRows: out var moveTrainRows,
				dirNormalRows: out var dirNormalRows,
				dirDownRows: out var dirDownRows);

			return new DailyDataset (
				trainRows: trainEligible.ToArray (),
				moveTrainRows: moveTrainRows.ToArray (),
				dirNormalRows: dirNormalRows.ToArray (),
				dirDownRows: dirDownRows.ToArray (),
				trainUntilUtc: trainUntilUtc);
			}
		}
	}
