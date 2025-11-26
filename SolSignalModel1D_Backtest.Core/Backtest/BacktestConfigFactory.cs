using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Фабрика конфигураций бэктеста.
	/// На этом шаге даём один baseline-профиль, который соответствует
	/// текущим захардкоженным настройкам.
	/// </summary>
	public static class BacktestConfigFactory
		{
		/// <summary>
		/// Возвращает baseline-конфиг бэктеста:
		/// - дневной SL/TP как в текущем коде;
		/// - набор политик const 2/3/5/10/15/50 × Cross/Isolated;
		/// - риск-политики risk_aware / ultra_safe (Cross).
		/// </summary>
		public static BacktestConfig CreateBaseline ()
			{
			var cfg = new BacktestConfig
				{
				DailyStopPct = 0.05,
				DailyTpPct = 0.03
				};

			// const 2/3/5/10/15/50 × Cross/Isolated
			double[] levels = { 2.0, 3.0, 5.0, 10.0, 15.0, 50.0 };

			foreach (var lev in levels)
				{
				var levLabel = lev % 1.0 == 0.0
					? $"{lev:0}"
					: $"{lev:0.##}";

				cfg.Policies.Add (new PolicyConfig
					{
					Name = $"const_{levLabel}x_cross",
					PolicyType = "const",
					Leverage = lev,
					MarginMode = MarginMode.Cross
					});

				cfg.Policies.Add (new PolicyConfig
					{
					Name = $"const_{levLabel}x_isolated",
					PolicyType = "const",
					Leverage = lev,
					MarginMode = MarginMode.Isolated
					});
				}

			// Риск-политика, которая адаптивно уменьшает плечо.
			cfg.Policies.Add (new PolicyConfig
				{
				Name = "risk_aware_cross",
				PolicyType = "risk_aware",
				Leverage = null,
				MarginMode = MarginMode.Cross
				});

			// Более консервативная политика.
			cfg.Policies.Add (new PolicyConfig
				{
				Name = "ultra_safe_cross",
				PolicyType = "ultra_safe",
				Leverage = null,
				MarginMode = MarginMode.Cross
				});

			return cfg;
			}
		}
	}
