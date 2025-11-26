using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	public static class LeveragePolicies
		{
		// просто константа
		public sealed class ConstPolicy : ILeveragePolicy
			{
			private readonly double _lev;
			public string Name { get; }

			public ConstPolicy ( string name, double lev )
				{
				Name = name;
				_lev = lev;
				}

			public double ResolveLeverage ( PredictionRecord rec ) => _lev;
			}

		/// <summary>
		/// if (rec.SlProb > 0.6 || rec.RegimeDown) → 1-2x, иначе 5x
		/// </summary>
		public sealed class RiskAwarePolicy : ILeveragePolicy
			{
			public string Name => "risk_aware";

			private const double SlThresh = 0.6;
			private const double LevSafe = 2.0;
			private const double LevMin = 1.0;
			private const double LevNorm = 5.0;

			public double ResolveLeverage ( PredictionRecord rec )
				{
				if (rec.RegimeDown && rec.SlProb > SlThresh)
					return LevMin;

				if (rec.RegimeDown)
					return LevSafe;

				if (rec.SlProb > SlThresh)
					return LevSafe;

				return LevNorm;
				}
			}

		/// <summary>
		/// торговать только "нормальные" дни:
		/// </summary>
		public sealed class UltraSafePolicy : ILeveragePolicy
			{
			public string Name => "ultra_safe";

			private const double LevGood = 3.0;

			public double ResolveLeverage ( PredictionRecord rec )
				{
				return LevGood;
				}
			}
		}
	}
