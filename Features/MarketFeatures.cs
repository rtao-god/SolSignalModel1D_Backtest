using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Features
	{
	public class MarketFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			var fngNorm = ((r.Fng == 0 ? 50.0 : r.Fng) - 50.0) / 50.0;

			ctx.Add (fngNorm);
			ctx.Add (r.DxyChg30);
			ctx.Add (r.GoldChg30);
			ctx.Add (r.SolRsiCentered / 100.0);
			ctx.Add (r.RsiSlope3 / 100.0);
			}
		}
	}
