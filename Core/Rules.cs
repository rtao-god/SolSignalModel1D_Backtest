namespace SolSignalModel1D_Backtest.Core
	{
	public static class Rules
		{
		public static bool IsCrashRule ( DataRow r )
			{
			return r.SolRet30 < -0.20 && r.SolRsiCentered < -25 && r.RsiSlope3 < 0;
			}

		public static bool IsGrowthRule ( DataRow r )
			{
			return r.SolRet30 > 0.05 && r.SolRsiCentered > 15 && r.RsiSlope3 > 0;
			}

		// более жёсткий даун — только для правил
		public static bool IsStrictDownForRules ( DataRow r )
			{
			return r.SolRet30 < -0.12 || r.BtcRet30 < -0.08;
			}
		}
	}
