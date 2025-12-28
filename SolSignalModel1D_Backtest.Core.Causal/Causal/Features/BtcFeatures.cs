namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Features
{
    public sealed class BtcFeatures : IFeatureBuilder<CausalDataRow>
    {
        public void Build(FeatureContext<CausalDataRow> ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var r = ctx.Row;

            ctx.Add(r.BtcRet1, nameof(r.BtcRet1));
            ctx.Add(r.BtcRet30, nameof(r.BtcRet30));
            ctx.Add(r.BtcVs200, nameof(r.BtcVs200));
        }
    }
}
