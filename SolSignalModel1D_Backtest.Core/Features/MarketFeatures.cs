using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public class MarketFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			var fngNorm = ((r.Causal.Fng == 0 ? 50.0 : r.Causal.Fng) - 50.0) / 50.0;

			ctx.Add (fngNorm);
			ctx.Add (r.Causal.DxyChg30);
			ctx.Add (r.Causal.GoldChg30);
			ctx.Add (r.Causal.SolRsiCentered / 100.0);
			ctx.Add (r.Causal.RsiSlope3 / 100.0);
			}
		}
	}
