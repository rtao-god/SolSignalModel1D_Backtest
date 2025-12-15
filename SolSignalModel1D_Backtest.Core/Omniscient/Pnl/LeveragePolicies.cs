using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Leverage;
using System;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Набор простых политик плеча.
	/// Архитектура:
	/// - PnL (реалистичный) должен использовать ICausalLeveragePolicy (без forward).
	/// - IOmniscientLeveragePolicy оставлен как отдельный контракт для what-if/диагностики.
	/// </summary>
	public static class LeveragePolicies
		{
		public sealed class ConstPolicy : ICausalLeveragePolicy, IOmniscientLeveragePolicy
			{
			private readonly double _lev;
			public string Name { get; }

			public ConstPolicy ( string name, double lev )
				{
				if (string.IsNullOrWhiteSpace (name))
					throw new ArgumentException ("policy name must not be empty", nameof (name));

				if (double.IsNaN (lev) || double.IsInfinity (lev) || lev <= 0.0)
					throw new ArgumentOutOfRangeException (nameof (lev), "leverage must be > 0");

				Name = name;
				_lev = lev;
				}

			public double ResolveLeverage ( CausalPredictionRecord causal ) => _lev;

			// Omniscient-вариант оставлен для совместимости/диагностики; по смыслу тут тот же const.
			public double ResolveLeverage ( BacktestRecord rec ) => _lev;
			}

		public sealed class RiskAwarePolicy : ICausalLeveragePolicy
			{
			public string Name => "risk_aware";

			private const double SlThresh = 0.6;
			private const double LevSafe = 2.0;
			private const double LevMin = 1.0;
			private const double LevNorm = 5.0;

			public double ResolveLeverage ( CausalPredictionRecord causal )
				{
				if (causal == null) throw new ArgumentNullException (nameof (causal));

				// SL-слой обязан быть посчитан до PnL; null тут — ошибка пайплайна.
				double slProb = causal.SlProb
					?? throw new InvalidOperationException ("[leverage] SlProb is null — SL layer missing.");

				if (causal.RegimeDown && slProb > SlThresh)
					return LevMin;

				if (causal.RegimeDown)
					return LevSafe;

				if (slProb > SlThresh)
					return LevSafe;

				return LevNorm;
				}
			}

		public sealed class UltraSafePolicy : ICausalLeveragePolicy, IOmniscientLeveragePolicy
			{
			public string Name => "ultra_safe";
			private const double LevGood = 3.0;

			public double ResolveLeverage ( CausalPredictionRecord causal )
				{
				if (causal == null) throw new ArgumentNullException (nameof (causal));
				return LevGood;
				}

			public double ResolveLeverage ( BacktestRecord rec )
				{
				if (rec == null) throw new ArgumentNullException (nameof (rec));
				return LevGood;
				}
			}
		}
	}
