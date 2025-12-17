using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Leverage;
using SolSignalModel1D_Backtest.Core.Trading.Leverage.Policies;

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
			// UltraSafe: специальные правила скипа.
			if (policy is UltraSafeLeveragePolicy)
				{
				if (rec.RegimeDown)
					return true;

				double slProb = rec.SlProb
					?? throw new System.InvalidOperationException ("[skip] SlProb is null — SL layer missing before PnL.");

				if (slProb > UltraSafeSlThresh)
					return true;
				}

			return false;
			}
		}
	}
