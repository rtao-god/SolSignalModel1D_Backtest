using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public sealed class PredictionRecord
		{
		public DateTime DateUtc { get; set; }

		public int TrueLabel { get; set; }

		public int PredLabel { get; set; }

		public bool PredMicroUp { get; set; }
		public bool PredMicroDown { get; set; }

		public bool FactMicroUp { get; set; }
		public bool FactMicroDown { get; set; }

		public double Entry { get; set; }
		public double MaxHigh24 { get; set; }
		public double MinLow24 { get; set; }
		public double Close24 { get; set; }

		public bool RegimeDown { get; set; }

		public string Reason { get; set; } = string.Empty;

		public double MinMove { get; set; }
		}
	}
