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

            // FNG: 0 — валидное значение индекса. Отсутствие должно быть null (и падать fail-fast).
            var fng = r.Fng ?? throw new InvalidOperationException($"[features] missing '{nameof(r.Fng)}' at entry={ctx.Stamp.EntryUtc:O}");
            var fngNorm = (fng - 50.0) / 50.0;
            ctx.Add(fngNorm);

            ctx.Add(r.DxyChg30, nameof(r.DxyChg30));
            ctx.Add(r.GoldChg30, nameof(r.GoldChg30));

            ctx.Add(r.SolRsiCentered, nameof(r.SolRsiCentered));
            ctx.Add(r.RsiSlope3, nameof(r.RsiSlope3));

            // Нормализация (как у тебя было): делим на 100.
            ctx.Features[^2] /= 100.0; // SolRsiCentered
            ctx.Features[^1] /= 100.0; // RsiSlope3
        }
    }
}
