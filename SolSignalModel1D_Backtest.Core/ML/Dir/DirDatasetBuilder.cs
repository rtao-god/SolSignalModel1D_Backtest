using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Daily;

namespace SolSignalModel1D_Backtest.Core.ML.Dir
	{
	/// <summary>
	/// DTO для dir-датасета: только дни с фактическим ходом (Label ∈ {0,2}),
	/// разложенные на NORMAL / DOWN.
	/// </summary>
	public sealed class DirDataset
		{
		public List<DataRow> DirNormalRows { get; }
		public List<DataRow> DirDownRows { get; }
		public DateTime TrainUntilUtc { get; }

		public DirDataset (
			List<DataRow> dirNormalRows,
			List<DataRow> dirDownRows,
			DateTime trainUntilUtc )
			{
			DirNormalRows = dirNormalRows ?? throw new ArgumentNullException (nameof (dirNormalRows));
			DirDownRows = dirDownRows ?? throw new ArgumentNullException (nameof (dirDownRows));
			TrainUntilUtc = trainUntilUtc;
			}
		}

	/// <summary>
	/// Dataset-builder для dir-слоя.
	/// На самом деле просто использует DailyDatasetBuilder, отключая балансировку move.
	/// </summary>
	public static class DirDatasetBuilder
		{
		public static DirDataset Build (
			List<DataRow> allRows,
			DateTime trainUntil,
			bool balanceDir,
			double balanceTargetFrac,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));

			var daily = DailyDatasetBuilder.Build (
				allRows: allRows,
				trainUntil: trainUntil,
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
