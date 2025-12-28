using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Trading
	{
	public static class Rules
		{
		public static bool IsCrashRule ( BacktestRecord r )
			{
			return r.Causal.SolRet30 < -0.20 && r.Causal.SolRsiCentered < -25 && r.Causal.RsiSlope3 < 0;
			}

		public static bool IsGrowthRule ( BacktestRecord r )
			{
			return r.Causal.SolRet30 > 0.05 && r.Causal.SolRsiCentered > 15 && r.Causal.RsiSlope3 > 0;
			}

		// более жёсткий даун — только для правил
		public static bool IsStrictDownForRules ( BacktestRecord r )
			{
			return r.Causal.SolRet30 < -0.12 || r.Causal.BtcRet30 < -0.08;
			}
		}
	}
