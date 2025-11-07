namespace SolSignalModel1D_Backtest.Features
	{
	public class VolatilityFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			ctx.Add (r.AtrPct);
			ctx.Add (r.DynVol);
			ctx.Add (r.RegimeDown ? 1.0 : 0.0);
			}
		}
	}
