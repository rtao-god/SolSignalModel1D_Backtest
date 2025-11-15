using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	/// <summary>
	/// Выход бинарного классификатора: предсказанная метка и вероятность.
	/// </summary>
	public sealed class BinaryPrediction
		{
		[ColumnName ("PredictedLabel")]
		public bool PredictedLabel { get; set; }

		[ColumnName ("Probability")]
		public float Probability { get; set; }

		[ColumnName ("Score")]
		public float Score { get; set; }
		}
	}
