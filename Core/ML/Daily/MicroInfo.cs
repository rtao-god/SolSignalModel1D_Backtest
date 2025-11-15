using System;

namespace SolSignalModel1D_Backtest.Core.ML.Daily
	{
	public sealed class MicroInfo
		{
		public bool Predicted { get; set; }
		public bool Up { get; set; }
		public bool ConsiderUp { get; set; }
		public bool ConsiderDown { get; set; }
		public float Prob { get; set; }
		public bool Correct { get; set; }
		}
	}
