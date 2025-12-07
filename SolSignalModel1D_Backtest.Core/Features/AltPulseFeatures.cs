using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Core.Features
	{
	public class AltPulseFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			ctx.Add (r.AltFracPos6h);
			ctx.Add (r.AltFracPos24h);
			ctx.Add (r.AltMedian24h);
			ctx.Add (r.AltReliable ? 1.0 : 0.0);
			}
		}
	}
