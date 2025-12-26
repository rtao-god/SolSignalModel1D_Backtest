namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats
	{
	/// <summary>
	/// Тип сегмента, по которому строятся независимые модельные метрики.
	/// ВАЖНО: сегменты режутся по контракту baseline-exit через TrainBoundary,
	/// чтобы не было boundary leakage.
	/// </summary>
	public enum ModelStatsSegmentKind
		{
		Unknown = 0,

		/// <summary>
		/// Только train-часть истории (baseline-exit <= trainUntilUtc).
		/// </summary>
		TrainOnly = 1,

		/// <summary>
		/// Только OOS-часть истории (baseline-exit > trainUntilUtc).
		/// </summary>
		OosOnly = 2,

		/// <summary>
		/// Последние N eligible дней.
		/// </summary>
		RecentWindow = 3,

		/// <summary>
		/// Полная история eligible дней (train + oos).
		/// </summary>
		FullHistory = 4
		}
	}
