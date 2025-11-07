using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Features
	{
	public class BtcFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;
			ctx.Add (r.BtcRet1);
			ctx.Add (r.BtcRet30);
			ctx.Add (r.BtcVs200);
			}
		}
	}
