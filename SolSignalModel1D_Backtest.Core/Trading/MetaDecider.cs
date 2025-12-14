using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using System;

namespace SolSignalModel1D_Backtest.Core.Trading
	{
	public static class MetaDecider
		{
		public static int AdjustByLiquidityAndFibo (
			BacktestRecord r,
			int predClass,
			double liqThresh = 0.004,
			double fiboThresh = 0.03 // фибо дальше → игнор
		)
			{
			int result = predClass;

			bool hasLiqUp = r.Causal.LiqUpRel > 0;
			bool hasLiqDown = r.Causal.LiqDownRel > 0;
			bool hasFiboUp = r.Causal.FiboUpRel > 0;
			bool hasFiboDown = r.Causal.FiboDownRel > 0;

			if (predClass == 1)
				{
				if (hasLiqUp &&
					(!hasLiqDown || r.Causal.LiqUpRel < r.Causal.LiqDownRel) &&
					r.Causal.LiqUpRel < liqThresh &&
					!r.RegimeDown)
					{
					result = 2;
					}
				else if (hasLiqDown &&
						 (!hasLiqUp || r.Causal.LiqDownRel < r.Causal.LiqUpRel) &&
						 r.Causal.LiqDownRel < liqThresh)
					{
					result = 0;
					}
				else
					{
					if (hasFiboUp &&
						(!hasFiboDown || r.Causal.FiboUpRel < r.Causal.FiboDownRel) &&
						r.Causal.FiboUpRel > 0 &&
						r.Causal.FiboUpRel < fiboThresh &&
						!r.RegimeDown)
						{
						result = 2;
						}
					else if (hasFiboDown &&
							 (!hasFiboUp || r.Causal.FiboDownRel < r.Causal.FiboUpRel) &&
							 r.Causal.FiboDownRel > 0 &&
							 r.Causal.FiboDownRel < fiboThresh)
						{
						result = 0;
						}
					}
				}

			return result;
			}
		}
	}
