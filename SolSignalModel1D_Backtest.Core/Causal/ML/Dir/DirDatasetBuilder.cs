using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Dir
	{
	/// <summary>
	/// DTO для dir-датасета: только дни с фактическим ходом (Label ∈ {0,2}),
	/// разложенные на NORMAL / DOWN.
	/// </summary>
	public sealed class DirDataset
		{
		public IReadOnlyList<BacktestRecord> DirNormalRows { get; }
		public IReadOnlyList<BacktestRecord> DirDownRows { get; }
		public DateTime TrainUntilUtc { get; }

		public DirDataset (
			IReadOnlyList<BacktestRecord> dirNormalRows,
			IReadOnlyList<BacktestRecord> dirDownRows,
			DateTime trainUntilUtc )
			{
			if (dirNormalRows == null) throw new ArgumentNullException (nameof (dirNormalRows));
			if (dirDownRows == null) throw new ArgumentNullException (nameof (dirDownRows));

			// Жёстко защищаемся от мутаций: храним копии как массивы.
			DirNormalRows = dirNormalRows.ToArray ();
			DirDownRows = dirDownRows.ToArray ();

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			TrainUntilUtc = trainUntilUtc;
			}
		}

	/// <summary>
	/// Dataset-builder для dir-слоя.
	/// Использует DailyDatasetBuilder, отключая балансировку move.
	/// </summary>
	public static class DirDatasetBuilder
		{
		public static DirDataset Build (
			IReadOnlyList<BacktestRecord> allRows,
			DateTime trainUntilUtc,
			bool balanceDir,
			double balanceTargetFrac,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			var daily = DailyDatasetBuilder.Build (
				allRows: allRows as List<BacktestRecord> ?? allRows.ToList (),
				trainUntil: trainUntilUtc,
				balanceMove: false,
				balanceDir: balanceDir,
				balanceTargetFrac: balanceTargetFrac,
				datesToExclude: datesToExclude);


			return new DirDataset (
				dirNormalRows: daily.DirNormalRows,
				dirDownRows: daily.DirDownRows,
				trainUntilUtc: daily.TrainUntilUtc);
			}
		}
	}
