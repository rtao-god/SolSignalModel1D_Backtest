namespace SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe
	{
	public abstract class OhlcCandleBase
		{
		public DateTime OpenTimeUtc { get; set; }
		public double Open { get; set; }
		public double High { get; set; }
		public double Low { get; set; }
		public double Close { get; set; }
		}

	public sealed class Candle1m : OhlcCandleBase { }
	public sealed class Candle1h : OhlcCandleBase { }
	public sealed class Candle6h : OhlcCandleBase { }

	}
