using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Micro
	{
	public sealed class MicroDataset
		{
		public IReadOnlyList<DataRow> TrainRows { get; }
		public IReadOnlyList<DataRow> MicroRows { get; }
		public DateTime TrainUntilUtc { get; }

		public MicroDataset (
			IReadOnlyList<DataRow> trainRows,
			IReadOnlyList<DataRow> microRows,
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
		public static MicroDataset Build (
			IReadOnlyList<DataRow> allRows,
			DateTime trainUntilUtc )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (allRows.Count == 0) throw new ArgumentException ("allRows must be non-empty.", nameof (allRows));

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			// Контракт: порядок уже стабилен (бутстрап/RowBuilder).
			SeriesGuards.EnsureStrictlyAscendingUtc (allRows, r => r.Date, "micro-dataset.allRows");

			var ordered = allRows as List<DataRow> ?? allRows.ToList ();

			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);
			var split = boundary.Split (ordered, r => r.Date);

			if (split.Excluded.Count > 0)
				{
				var sample = split.Excluded
					.Take (Math.Min (10, split.Excluded.Count))
					.Select (r => r.Date.ToString ("O"));

				throw new InvalidOperationException (
					$"[micro-dataset] Found excluded days (baseline-exit undefined). " +
					$"count={split.Excluded.Count}. sample=[{string.Join (", ", sample)}].");
				}

			var trainRows = split.Train;

			// Порядок сохраняется фильтрацией.
			var microRowsList = trainRows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.ToList ();

			var trainFrozen = (trainRows as List<DataRow> ?? trainRows.ToList ()).ToArray ();
			var microFrozen = microRowsList.ToArray ();

			return new MicroDataset (trainFrozen, microFrozen, trainUntilUtc);
			}
		}
	}
