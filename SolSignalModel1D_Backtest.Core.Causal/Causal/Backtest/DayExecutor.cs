using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using CausalDataRow = SolSignalModel1D_Backtest.Core.Causal.Data.CausalDataRow;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Backtest
	{
	/// <summary>
	/// Тонкая обёртка каузального исполнения:
	/// принимает только CausalDataRow и возвращает CausalPredictionRecord.
	/// </summary>
	public static class DayExecutor
		{
		public static CausalPredictionRecord ProcessDay (
			CausalDataRow dayRow,
			PredictionEngine dailyEngine )
			{
			if (dayRow == null) throw new ArgumentNullException (nameof (dayRow));
			if (dailyEngine == null) throw new ArgumentNullException (nameof (dailyEngine));

			return dailyEngine.PredictCausal (dayRow);
			}
		}
	}
