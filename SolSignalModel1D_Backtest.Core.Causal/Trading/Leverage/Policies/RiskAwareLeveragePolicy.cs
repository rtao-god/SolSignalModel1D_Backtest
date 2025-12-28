using SolSignalModel1D_Backtest.Core.Causal.Data;
using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage.Policies
	{
	/// <summary>
	/// Risk-aware плечо по каузальному слою.
	///
	/// Идея: если SL-слой пометил день как high-risk (SlHighDecision==true),
	/// используем пониженное плечо. Иначе — нормальное.
	///
	/// Важно: политика НЕ имеет доступа к forward-окну и не может принимать решения “задним числом”.
	/// </summary>
	public sealed class RiskAwareLeveragePolicy : ICausalLeveragePolicy
		{
		public string Name { get; }

		private readonly double _normalLeverage;
		private readonly double _highRiskLeverage;

		public RiskAwareLeveragePolicy ( string name, double normalLeverage, double highRiskLeverage )
			{
			Name = string.IsNullOrWhiteSpace (name) ? "risk_aware" : name;

			if (!double.IsFinite (normalLeverage) || normalLeverage <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (normalLeverage), normalLeverage, "Leverage must be finite and > 0.");

			if (!double.IsFinite (highRiskLeverage) || highRiskLeverage <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (highRiskLeverage), highRiskLeverage, "Leverage must be finite and > 0.");

			// Инвариант: high-risk должен быть <= normal, иначе политика теряет смысл.
			if (highRiskLeverage > normalLeverage)
				throw new ArgumentException ("highRiskLeverage must be <= normalLeverage.");

			_normalLeverage = normalLeverage;
			_highRiskLeverage = highRiskLeverage;
			}

		public double ResolveLeverage ( CausalPredictionRecord causal )
			{
			if (causal == null) throw new ArgumentNullException (nameof (causal));

			// bool? трактуем строго: только true = high-risk. null/false => не high-risk.
			if (causal.SlHighDecision == true)
				return _highRiskLeverage;

			return _normalLeverage;
			}
		}
	}
