using System;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Сэмпл для сильного отката (Model A).
	/// Label = true → "отложка реально спасла плохой день".
	/// </summary>
	public sealed class PullbackContinuationSample
		{
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public bool Label { get; set; }

		/// <summary>Нужен для каузального среза.</summary>
		public DateTime EntryUtc { get; set; }
		}
	}
