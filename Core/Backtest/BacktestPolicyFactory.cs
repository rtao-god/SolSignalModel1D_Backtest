using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Преобразует логические PolicyConfig в реальные PolicySpec + ILeveragePolicy,
	/// которые используются RollingLoop/PnL-движком.
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

		private static ILeveragePolicy CreatePolicy ( PolicyConfig cfg )
			{
			if (cfg == null) throw new ArgumentNullException (nameof (cfg));

			switch (cfg.PolicyType)
				{
				case "const":
					if (!cfg.Leverage.HasValue)
						throw new InvalidOperationException (
							$"Policy '{cfg.Name}': Leverage must be specified for 'const' type.");

					return new LeveragePolicies.ConstPolicy (cfg.Name, cfg.Leverage.Value);

				case "risk_aware":
					return new LeveragePolicies.RiskAwarePolicy ();

				case "ultra_safe":
					return new LeveragePolicies.UltraSafePolicy ();

				default:
					// На будущее: можно сделать регистрацию пользовательских политик.
					throw new NotSupportedException (
						$"Unknown policy type '{cfg.PolicyType}' for policy '{cfg.Name}'.");
				}
			}
		}
	}
