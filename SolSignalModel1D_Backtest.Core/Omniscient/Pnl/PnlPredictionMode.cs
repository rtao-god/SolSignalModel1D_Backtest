namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	public enum PnlPredictionMode
		{
		/// <summary>
		/// Использовать только PredLabel и микро-флаги (текущая логика).
		/// </summary>
		DayOnly,

		/// <summary>
		/// Использовать агрегированный слой Day+Micro (PredLabel_DayMicro / Prob*_DayMicro).
		/// </summary>
		DayPlusMicro,

		/// <summary>
		/// Использовать полный стек Day+Micro+SL (Prob*_Total).
		/// </summary>
		DayPlusMicroPlusSl
		}
	}
