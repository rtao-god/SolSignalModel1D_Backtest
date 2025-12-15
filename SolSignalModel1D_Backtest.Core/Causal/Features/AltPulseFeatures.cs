namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public sealed class AltPulseFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;

			ctx.Add (r.Causal.AltFracPos6h);
			ctx.Add (r.Causal.AltFracPos24h);
			ctx.Add (r.Causal.AltMedian24h);
			ctx.Add01 (r.Causal.AltReliable);
			}
		}
	}
