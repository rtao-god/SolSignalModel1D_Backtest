using SolSignalModel1D_Backtest.Core.Causal.Data;
using System;

namespace SolSignalModel1D_Backtest.Core.Trading
	{
	public static class MetaDecider
		{
		public static int AdjustByLiquidityAndFibo (
			DataRow r,
			int predClass,
			double liqThresh = 0.004,
			double fiboThresh = 0.03 // фибо дальше → игнор
		)
			{
			int result = predClass;

			bool hasLiqUp = r.LiqUpRel > 0;
			bool hasLiqDown = r.LiqDownRel > 0;
			bool hasFiboUp = r.FiboUpRel > 0;
			bool hasFiboDown = r.FiboDownRel > 0;

			if (predClass == 1)
				{
				if (hasLiqUp &&
					(!hasLiqDown || r.LiqUpRel < r.LiqDownRel) &&
					r.LiqUpRel < liqThresh &&
					!r.RegimeDown)
					{
					result = 2;
					}
				else if (hasLiqDown &&
						 (!hasLiqUp || r.LiqDownRel < r.LiqUpRel) &&
						 r.LiqDownRel < liqThresh)
					{
					result = 0;
					}
				else
					{
					if (hasFiboUp &&
						(!hasFiboDown || r.FiboUpRel < r.FiboDownRel) &&
						r.FiboUpRel > 0 &&
						r.FiboUpRel < fiboThresh &&
						!r.RegimeDown)
						{
						result = 2;
						}
					else if (hasFiboDown &&
							 (!hasFiboUp || r.FiboDownRel < r.FiboUpRel) &&
							 r.FiboDownRel > 0 &&
							 r.FiboDownRel < fiboThresh)
						{
						result = 0;
						}
					}
				}

			return result;
			}
		}
	}
