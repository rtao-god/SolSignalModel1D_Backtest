using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Features
	{
	// фичи именно по SOL — только то, что реально есть в DataRow
	public sealed class SolFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;

			// длинный контекст
			ctx.Add (r.SolRet30);    // 30 окон назад
									 // средний и короткий
			ctx.Add (r.SolRet3);
			ctx.Add (r.SolRet1);

			// флаг "это утро NY" — полезно, если ты потом смешиваешь
			ctx.Add (r.IsMorning ? 1.0 : 0.0);
			}
		}
	}
