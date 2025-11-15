using System;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Сэмпл для мелкого улучшения (Model B).
	/// Label = true → "отложка исполнилась и не была хуже 12:00 на плохом дне".
	/// </summary>
	public sealed class SmallImprovementSample
		{
		[VectorType (MlSchema.FeatureCount)]
		public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

		public bool Label { get; set; }

		public DateTime EntryUtc { get; set; }
		}
	}
