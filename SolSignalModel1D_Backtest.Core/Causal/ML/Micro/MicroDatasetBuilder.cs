using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Micro
	{
	public sealed class MicroDataset
		{
		public IReadOnlyList<LabeledCausalRow> TrainRows { get; }
		public IReadOnlyList<LabeledCausalRow> MicroRows { get; }
		public DateTime TrainUntilUtc { get; }

		public MicroDataset (
			IReadOnlyList<LabeledCausalRow> trainRows,
			IReadOnlyList<LabeledCausalRow> microRows,
			DateTime trainUntilUtc )
			{
			TrainRows = trainRows ?? throw new ArgumentNullException (nameof (trainRows));
			MicroRows = microRows ?? throw new ArgumentNullException (nameof (microRows));

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			TrainUntilUtc = trainUntilUtc;
			}
		}

	public static class MicroDatasetBuilder
		{
		private static DateTime DayKeyUtc ( LabeledCausalRow r ) => CausalTimeKey.DayKeyUtc (r);

		public static MicroDataset Build (
			IReadOnlyList<LabeledCausalRow> allRows,
			DateTime trainUntilUtc )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (allRows.Count == 0) throw new ArgumentException ("allRows must be non-empty.", nameof (allRows));

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			SeriesGuards.EnsureStrictlyAscendingUtc (allRows, r => DayKeyUtc (r), "micro-dataset.allRows");

			var ordered = allRows as List<LabeledCausalRow> ?? allRows.ToList ();

			var boundary = new TrainBoundary (trainUntilUtc, NyWindowing.NyTz);
			var split = boundary.Split (ordered, r => DayKeyUtc (r));

			if (split.Excluded.Count > 0)
				{
				var sample = split.Excluded
					.Take (Math.Min (10, split.Excluded.Count))
					.Select (r => DayKeyUtc (r).ToString ("O"));

				throw new InvalidOperationException (
					$"[micro-dataset] Found excluded days (baseline-exit undefined). " +
					$"count={split.Excluded.Count}. sample=[{string.Join (", ", sample)}].");
				}

			var trainRows = split.Train;

			var microRowsList = trainRows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.ToList ();

			var trainFrozen = (trainRows as List<LabeledCausalRow> ?? trainRows.ToList ()).ToArray ();
			var microFrozen = microRowsList.ToArray ();

			return new MicroDataset (trainFrozen, microFrozen, trainUntilUtc);
			}
		}
	}
