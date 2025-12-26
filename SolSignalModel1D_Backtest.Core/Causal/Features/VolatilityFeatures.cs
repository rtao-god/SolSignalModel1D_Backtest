using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
{
    public sealed class VolatilityFeatures : IFeatureBuilder<CausalDataRow>
    {
        public void Build(FeatureContext<CausalDataRow> ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var r = ctx.Row;

            ctx.Add(r.AtrPct, nameof(r.AtrPct));
            ctx.Add(r.DynVol, nameof(r.DynVol));
            ctx.Add01(r.RegimeDown, nameof(r.RegimeDown));
        }
    }
}
