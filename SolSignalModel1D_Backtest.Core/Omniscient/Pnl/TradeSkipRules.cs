using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Leverage;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Правила полного «скипа» дня для отдельных политик.
	/// ВАЖНО: любые решения должны быть каузальными (без forward-фактов).
	/// </summary>
	public static class TradeSkipRules
		{
		private const double UltraSafeSlThresh = 0.6;

		public static bool ShouldSkipDay ( BacktestRecord rec, ICausalLeveragePolicy policy )
			{
			// Ultra-safe: не торгуем дни, где режим вниз либо SL-риск высокий.
			if (policy is LeveragePolicies.UltraSafePolicy)
				{
				if (rec.RegimeDown)
					return true;

				// SlProb обязан быть посчитан до PnL; null — это pipeline-bug.
				double slProb = rec.SlProb
					?? throw new System.InvalidOperationException ("[skip] SlProb is null — SL layer missing before PnL.");

				if (slProb > UltraSafeSlThresh)
					return true;
				}

			return false;
			}
		}
	}
