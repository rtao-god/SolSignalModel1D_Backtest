using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	public static class LeveragePolicies
		{
		/// <summary>
		/// Политика с фиксированным плечом.
		/// </summary>
		public sealed class ConstPolicy : ILeveragePolicy
			{
			private readonly double _lev;
			public string Name { get; }

			public ConstPolicy ( string name, double lev )
				{
				Name = name;
				_lev = lev;
				}

			public double ResolveLeverage ( BacktestRecord rec ) => _lev;
			}

		/// <summary>
		/// Простая риск-aware политика:
		/// - если RegimeDown и/или SlProb высокие → пониженное плечо;
		/// - иначе нормальное плечо.
		/// </summary>
		public sealed class RiskAwarePolicy : ILeveragePolicy
			{
			public string Name => "risk_aware";

			private const double SlThresh = 0.6;
			private const double LevSafe = 2.0;
			private const double LevMin = 1.0;
			private const double LevNorm = 5.0;

			public double ResolveLeverage ( BacktestRecord rec )
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
		/// Политика "торговать только относительно спокойные дни".
		/// Детальная фильтрация реализуется в TradeSkipRules.ShouldSkipDay.
		/// </summary>
		public sealed class UltraSafePolicy : ILeveragePolicy
			{
			public string Name => "ultra_safe";

			private const double LevGood = 3.0;

			public double ResolveLeverage ( BacktestRecord rec )
				{
				return LevGood;
				}
			}
		}
	}
