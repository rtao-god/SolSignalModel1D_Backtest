using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed
	{
	public sealed class TargetLevelSample
		{
		public int Label { get; set; }

		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public DateTime EntryUtc { get; set; }

		[NoColumn]
		public BacktestRecord? Record { get; set; }

		[NoColumn]
		public CausalPredictionRecord Causal =>
			Record?.Causal ?? throw new InvalidOperationException ("[ml] TargetLevelSample.Record is null");

		[NoColumn]
		public ForwardOutcomes Forward =>
			Record?.Forward ?? throw new InvalidOperationException ("[ml] TargetLevelSample.Record is null");
		}
	}
