using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
{
    public sealed class MarketFeatures : IFeatureBuilder<CausalDataRow>
    {
        public void Build(FeatureContext<CausalDataRow> ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var r = ctx.Row;

            ctx.Add(r.FngNorm, nameof(r.FngNorm));
            ctx.Add(r.DxyChg30, nameof(r.DxyChg30));
            ctx.Add(r.GoldChg30, nameof(r.GoldChg30));

            ctx.Add(r.SolRsiCenteredScaled, nameof(r.SolRsiCenteredScaled));
            ctx.Add(r.RsiSlope3Scaled, nameof(r.RsiSlope3Scaled));
        }
    }
}
