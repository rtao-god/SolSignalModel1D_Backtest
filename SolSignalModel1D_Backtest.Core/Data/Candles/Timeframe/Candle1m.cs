using System;

namespace SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe
	{
	public sealed class Candle1m
		{
		public DateTime OpenTimeUtc { get; set; }
		public double Open { get; set; }
		public double High { get; set; }
		public double Low { get; set; }
		public double Close { get; set; }
		}
	}
