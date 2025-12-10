using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Правила полного «скипа» дня для отдельных политик.
	/// </summary>
	public static class TradeSkipRules
		{
		// Порог из UltraSafePolicy: "торговать только нормальные дни".
		private const double UltraSafeSlThresh = 0.6;

		/// <summary>
		/// Вернуть true, если для данной политики этот день не должен торговаться.
		/// </summary>
		public static bool ShouldSkipDay ( BacktestRecord rec, ILeveragePolicy policy )
			{
			// Ultra-safe: не торгуем дни, где режим вниз либо SL-риск высокий.
			if (policy is LeveragePolicies.UltraSafePolicy)
				{
				if (rec.RegimeDown)
					return true;

				if (rec.SlProb > UltraSafeSlThresh)
					return true;
				}

			return false;
			}
		}
	}
