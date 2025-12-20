using System;
using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	public sealed class MlBinaryOutput
		{
		[ColumnName ("PredictedLabel")]
		public bool PredictedLabel { get; set; }

		[ColumnName ("Score")]
		public float Score { get; set; }

		[ColumnName ("Probability")]
		public float Probability { get; set; }
		}

	public sealed class SlHitSample
		{
		public bool Label { get; set; }

		[VectorType (SlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[SlSchema.FeatureCount];

		public DateTime EntryUtc { get; set; }

		public float Weight { get; set; }
		}

	public sealed class SlHitPrediction
		{
		[ColumnName ("PredictedLabel")]
		public bool PredictedLabel { get; set; }

		[ColumnName ("Score")]
		public float Score { get; set; }

		[ColumnName ("Probability")]
		public float Probability { get; set; }
		}
	}
