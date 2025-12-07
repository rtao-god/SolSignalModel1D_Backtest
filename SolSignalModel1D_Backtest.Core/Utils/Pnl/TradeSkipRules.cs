using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// Правила «скипа» дня для политик (в частности Ultra-safe, который может скипать дни).
	/// </summary>
	public static class TradeSkipRules
		{
		// Порог из UltraSafePolicy: "торговать только нормальные дни".
		private const double UltraSafeSlThresh = 0.6;

		/// <summary>
		/// Вернуть true, если для данной политики этот день не должен торговаться вообще.
		/// </summary>
		public static bool ShouldSkipDay ( PredictionRecord rec, ILeveragePolicy policy )
			{
			// Ultra-safe политика: торгуем только "нормальные" дни
			// (НЕ RegimeDown и SlProb <= 0.6).
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
