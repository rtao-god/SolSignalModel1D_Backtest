namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{
	/// <summary>
	/// Результат расчёта minMove.
	/// </summary>
	public sealed class MinMoveResult
		{
		public double MinMove { get; set; }
		public double LocalVol { get; set; }
		public double EwmaVol { get; set; }
		public double QuantileUsed { get; set; }

		public DateTime AsOfUtc { get; init; }

		// Под асимметрию оставлены, сейчас null.
		public double? MinMoveUp { get; init; }
		public double? MinMoveDown { get; init; }

		public double FlatShare30d { get; init; }
		public double FlatShare90d { get; init; }
		public double EconFloorUsed { get; init; }
		public double EwmaVolUsed { get; init; }
		public bool RegimeDown { get; init; }

		public string Notes { get; init; } = string.Empty;
		}
	}