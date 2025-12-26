namespace SolSignalModel1D_Backtest.Core.Trading
	{
	public enum TradeOutcome
		{
		Ignored = 0,  // и TP и SL в одной 1h свечке — не считаем
		TpHit = 1,
		SlHit = 2,
		Closed = 3   // ни TP, ни SL — закрылись по close 24h
		}
	}
