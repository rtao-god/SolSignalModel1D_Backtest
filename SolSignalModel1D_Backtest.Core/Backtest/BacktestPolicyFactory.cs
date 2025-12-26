using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Trading.Leverage;
using SolSignalModel1D_Backtest.Core.Trading.Leverage.Policies;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Преобразует PolicyConfig в реальные PolicySpec + ICausalLeveragePolicy,
	/// которые используются RollingLoop/PnL-движком.
	///
	/// Инвариант: фабрика НЕ создаёт политик, которым нужны forward-факты.
	/// Любая IOmniscient-политика здесь запрещена архитектурно.
	/// </summary>
	public static class BacktestPolicyFactory
		{
		public static List<RollingLoop.PolicySpec> BuildPolicySpecs ( BacktestConfig config )
			{
			if (config == null) throw new ArgumentNullException (nameof (config));

			var list = new List<RollingLoop.PolicySpec> (config.Policies.Count);

			foreach (var pc in config.Policies)
				{
				var policy = CreatePolicy (pc);

				list.Add (new RollingLoop.PolicySpec
					{
					Name = pc.Name,
					Policy = policy,
					Margin = pc.MarginMode
					});
				}

			return list;
			}

		private static ICausalLeveragePolicy CreatePolicy ( PolicyConfig cfg )
			{
			if (cfg == null) throw new ArgumentNullException (nameof (cfg));

			switch (cfg.PolicyType)
				{
				case "const":
					if (!cfg.Leverage.HasValue)
						throw new InvalidOperationException (
							$"Policy '{cfg.Name}': Leverage must be specified for 'const' type.");

					return new ConstLeveragePolicy (cfg.Name, cfg.Leverage.Value);

				case "risk_aware":
					return new RiskAwareLeveragePolicy (name: cfg.Name, normalLeverage: 10.0, highRiskLeverage: 3.0);

				case "ultra_safe":
					// Жёстко консервативная политика: плечо фиксировано низкое, каузально.
					return new UltraSafeLeveragePolicy (name: cfg.Name, leverage: 2.0);

				default:
					throw new NotSupportedException (
						$"Unknown policy type '{cfg.PolicyType}' for policy '{cfg.Name}'.");
				}
			}
		}
	}
