namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{
	/// <summary>
	/// Минимальный исторический факт для MinMove:
	/// - дата дня (UTC),
	/// - реализованная амплитуда path (в долях, например 0.035 = 3.5%).
	/// </summary>
	public readonly record struct MinMoveHistoryRow (
		DateTime DateUtc,
		double RealizedPathAmpPct );
	}
