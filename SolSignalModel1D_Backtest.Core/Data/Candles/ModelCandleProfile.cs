using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// Что нужно конкретной части пайплайна.
	/// Например: "daily" – sol 6h + btc 6h; "sl" – sol 1h.
	/// </summary>
	public sealed class ModelCandleProfile ( string name, IReadOnlyList<(string, CandleTimeframe)> reqs )
		{
		public string Name { get; } = name;
		public IReadOnlyList<(string symbol, CandleTimeframe tf)> Requirements { get; } = reqs;
		}
	}
