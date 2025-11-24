using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;

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
				var policy = new LeveragePolicies.ConstPolicy (name, lev);
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

			// риск-осознанная
			var riskAware = new LeveragePolicies.RiskAwarePolicy ();
			list.Add (new RollingLoop.PolicySpec { Name = $"{riskAware.Name} Cross", Policy = riskAware, Margin = MarginMode.Cross });
			list.Add (new RollingLoop.PolicySpec { Name = $"{riskAware.Name} Isolated", Policy = riskAware, Margin = MarginMode.Isolated });

			// ультра-безопасная
			var ultraSafe = new LeveragePolicies.UltraSafePolicy ();
			list.Add (new RollingLoop.PolicySpec { Name = $"{ultraSafe.Name} Cross", Policy = ultraSafe, Margin = MarginMode.Cross });
			list.Add (new RollingLoop.PolicySpec { Name = $"{ultraSafe.Name} Isolated", Policy = ultraSafe, Margin = MarginMode.Isolated });

			return list;
			}
		}
	}
