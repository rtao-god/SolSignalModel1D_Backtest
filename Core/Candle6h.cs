using System;

namespace SolSignalModel1D_Backtest.Core
	{
	public sealed class Candle6h
		{
		public DateTime OpenTimeUtc { get; set; }
		public double Open { get; set; }
		public double High { get; set; }
		public double Low { get; set; }
		public double Close { get; set; }
		}
	}
