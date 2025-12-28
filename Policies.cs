using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using LeveragePolicies = SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage.Policies;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static List<RollingLoop.PolicySpec> BuildPolicies ()
			{
			var list = new List<RollingLoop.PolicySpec> ();

			void AddConst ( double lev )
				{
				var name = $"const_{lev:0.#}x";
				var policy = new LeveragePolicies.ConstLeveragePolicy (name, lev);

				list.Add (new RollingLoop.PolicySpec { Name = $"{name} Cross", Policy = policy, Margin = MarginMode.Cross });
				list.Add (new RollingLoop.PolicySpec { Name = $"{name} Isolated", Policy = policy, Margin = MarginMode.Isolated });
				}

			// фиксированные плечи
			AddConst (2.0);
			AddConst (3.0);
			AddConst (5.0);
			AddConst (10.0);
			AddConst (15.0);
			AddConst (50.0);

			// Risk-aware / Ultra-safe: значения плеча нужно задать явно.
			const double RiskAwareNormalLev = 10.0;
			const double RiskAwareHighRiskLev = 3.0;
			const double UltraSafeLev = 2.0;

			var riskAware = new LeveragePolicies.RiskAwareLeveragePolicy (
				name: "risk_aware",
				normalLeverage: RiskAwareNormalLev,
				highRiskLeverage: RiskAwareHighRiskLev);

			list.Add (new RollingLoop.PolicySpec { Name = $"{riskAware.Name} Cross", Policy = riskAware, Margin = MarginMode.Cross });
			list.Add (new RollingLoop.PolicySpec { Name = $"{riskAware.Name} Isolated", Policy = riskAware, Margin = MarginMode.Isolated });

			var ultraSafe = new LeveragePolicies.UltraSafeLeveragePolicy (
				name: "ultra_safe",
				leverage: UltraSafeLev);

			list.Add (new RollingLoop.PolicySpec { Name = $"{ultraSafe.Name} Cross", Policy = ultraSafe, Margin = MarginMode.Cross });
			list.Add (new RollingLoop.PolicySpec { Name = $"{ultraSafe.Name} Isolated", Policy = ultraSafe, Margin = MarginMode.Isolated });

			return list;
			}
		}
	}
