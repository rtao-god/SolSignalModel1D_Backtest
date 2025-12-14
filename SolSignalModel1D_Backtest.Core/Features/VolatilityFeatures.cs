namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public class VolatilityFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			ctx.Add (r.Causal.AtrPct);
			ctx.Add (r.Causal.DynVol);
			ctx.Add (r.RegimeDown ? 1.0 : 0.0);
			}
		}
	}
