using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Features
{
    public sealed class SolFeatures : IFeatureBuilder<CausalDataRow>
    {
        public void Build(FeatureContext<CausalDataRow> ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var r = ctx.Row;

            ctx.Add(r.SolRet30, nameof(r.SolRet30));
            ctx.Add(r.SolRet3, nameof(r.SolRet3));
            ctx.Add(r.SolRet1, nameof(r.SolRet1));

            ctx.Add01(r.IsMorning, nameof(r.IsMorning));
        }
    }
}
