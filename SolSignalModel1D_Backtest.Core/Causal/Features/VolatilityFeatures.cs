using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public sealed class VolatilityFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			var r = ctx.Row;
			ctx.Add (r.Causal.AtrPct);
			ctx.Add (r.Causal.DynVol);
			ctx.Add01 (r.RegimeDown);
			}
		}
	}
