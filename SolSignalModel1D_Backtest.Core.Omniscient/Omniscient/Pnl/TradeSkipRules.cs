using SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage.Policies;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
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
					?? throw new InvalidOperationException ("[skip] SlProb is null — SL layer missing before PnL.");

				if (slProb > UltraSafeSlThresh)
					return true;
				}

			return false;
			}
		}
	}
