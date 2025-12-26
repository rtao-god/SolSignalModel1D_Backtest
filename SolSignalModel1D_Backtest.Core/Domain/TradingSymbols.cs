namespace SolSignalModel1D_Backtest.Core.Domain
	{
	/// <summary>
	/// Централизованные имена тикеров:
	/// INTERNAL — код биржи / в файлах,
	/// DISPLAY  — человекочитаемое имя для консоли, отчётов и фронта.
	/// </summary>
	public static class TradingSymbols
		{
		public const string SolUsdtInternal = "SOLUSDT";
		public const string SolUsdtDisplay = "SOL/USDT";

		public const string BtcUsdtInternal = "BTCUSDT";
		public const string BtcUsdtDisplay = "BTC/USDT";

		public const string PaxgUsdtInternal = "PAXGUSDT";
		public const string PaxgUsdtDisplay = "PAXG/USDT";
		}
	}
