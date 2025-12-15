using System;
using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public sealed class MarketFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;

			// FNG: 0 — валидное значение индекса, поэтому "0 => 50" недопустимо.
			// Если источника нет — это null, и это должны увидеть явно.
			double fng = r.Causal.GetFeatureOrThrow (r.Causal.Fng, nameof (r.Causal.Fng));
			var fngNorm = (fng - 50.0) / 50.0;
			ctx.Add (fngNorm);

			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.DxyChg30, nameof (r.Causal.DxyChg30)));
			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.GoldChg30, nameof (r.Causal.GoldChg30)));

			// Эти метрики уже нормализуются как у тебя.
			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.SolRsiCentered, nameof (r.Causal.SolRsiCentered)) / 100.0);
			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.RsiSlope3, nameof (r.Causal.RsiSlope3)) / 100.0);
			}
		}
	}
