namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{

	/// <summary>
	/// Состояние адаптивного minMove — таскается по дням вперёд.
	/// </summary>
	public sealed class MinMoveState
		{
		/// <summary>EWMA по локальной волатильности (в долях).</summary>
		public double EwmaVol { get; set; }

		/// <summary>Текущий целевой квантиль path-амплитуды.</summary>
		public double QuantileQ { get; set; }

		/// <summary>Дата последнего пересчёта квантиля.</summary>
		public DateTime LastQuantileTune { get; set; }
		}
	}
