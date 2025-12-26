namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public sealed class CandleFetchConfig
		{
		public bool Need1m { get; set; } = true;
		public bool Need1h { get; set; } = true;
		public bool Need6h { get; set; } = true;
		}
	}
