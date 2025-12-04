namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	public static class SlUtils
		{
		// простая версия — порог по MinMove
		public static bool IsStrongByMinMove ( double dayMinMove )
			{
			// защищаемся от мусора
			if (dayMinMove <= 0 || double.IsNaN (dayMinMove))
				return false;

			// базовая эвристика:
			//   <= 2%   → слабый
			//   >= 3.5% → сильный
			//   между   → средний, можно отнести к слабым или сильным, по вкусу
			if (dayMinMove >= 0.035) return true;   // strong
			if (dayMinMove <= 0.020) return false;  // weak

			// серую зону можно пока считать слабой
			return false;
			}
		}
	}
