namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats
	{
	/// <summary>
	/// Режим запуска, в котором собираются/печатаются модельные статистики.
	/// Нужен для метаданных отчёта и для согласованности пайплайна.
	/// </summary>
	public enum ModelRunKind
		{
		/// <summary>
		/// Значение по умолчанию, если режим не задан явно.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// Аналитический режим (консольный backtest/отчёты/принтеры).
		/// </summary>
		Analytics = 1
		}
	}
