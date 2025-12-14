using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	// фичи именно по SOL — только то, что реально есть в BacktestRecord
	public sealed class SolFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;

			// длинный контекст
			ctx.Add (r.Causal.SolRet30);    // 30 окон назад
									 // средний и короткий
			ctx.Add (r.Causal.SolRet3);
			ctx.Add (r.Causal.SolRet1);

			// флаг "это утро NY" — полезно
			ctx.Add (r.Causal.IsMorning ? 1.0 : 0.0);
			}
		}
	}
