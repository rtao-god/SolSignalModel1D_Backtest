using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public class BtcFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			ctx.Add (r.Causal.BtcRet1);
			ctx.Add (r.Causal.BtcRet30);
			ctx.Add (r.Causal.BtcVs200);
			}
		}
	}
