using SolSignalModel1D_Backtest.Core.Causal.Data;
using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage.Policies
	{
	/// <summary>
	/// Константное плечо.
	/// Инвариант: решение не зависит ни от каких данных, кроме конфигурации.
	/// </summary>
	public sealed class ConstLeveragePolicy : ICausalLeveragePolicy
		{
		public string Name { get; }

		private readonly double _leverage;

		public ConstLeveragePolicy ( string name, double leverage )
			{
			Name = string.IsNullOrWhiteSpace (name) ? "const" : name;

			if (!double.IsFinite (leverage) || leverage <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (leverage), leverage, "Leverage must be finite and > 0.");

			_leverage = leverage;
			}

		public double ResolveLeverage ( CausalPredictionRecord causal )
			{
			if (causal == null) throw new ArgumentNullException (nameof (causal));
			return _leverage;
			}
		}
	}
