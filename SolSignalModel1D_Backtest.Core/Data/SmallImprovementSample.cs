using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public sealed class SmallImprovementSample
		{
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public bool Label { get; set; }

		public DateTime EntryUtc { get; set; }

		[NoColumn]
		public BacktestRecord? Record { get; set; }

		[NoColumn]
		public CausalPredictionRecord Causal =>
			Record?.Causal ?? throw new InvalidOperationException ("[ml] SmallImprovementSample.Record is null");

		[NoColumn]
		public ForwardOutcomes Forward =>
			Record?.Forward ?? throw new InvalidOperationException ("[ml] SmallImprovementSample.Record is null");
		}
	}
