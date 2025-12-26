using SolSignalModel1D_Backtest.Core.Causal.Data;
using System;

namespace SolSignalModel1D_Backtest.Core.Trading.Leverage.Policies
	{
	/// <summary>
	/// Максимально консервативная политика: фиксированное маленькое плечо.
	/// Полезна как baseline для sanity и сравнения стратегий.
	/// </summary>
	public sealed class UltraSafeLeveragePolicy : ICausalLeveragePolicy
		{
		public string Name { get; }

		private readonly double _leverage;

		public UltraSafeLeveragePolicy ( string name, double leverage )
			{
			Name = string.IsNullOrWhiteSpace (name) ? "ultra_safe" : name;

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
