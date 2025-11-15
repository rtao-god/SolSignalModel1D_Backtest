using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// Что нужно конкретной части пайплайна.
	/// Например: "daily" – sol 6h + btc 6h; "sl" – sol 1h.
	/// </summary>
	public sealed class ModelCandleProfile
		{
		public string Name { get; }
		public IReadOnlyList<(string symbol, CandleTimeframe tf)> Requirements { get; }

		public ModelCandleProfile ( string name, IReadOnlyList<(string, CandleTimeframe)> reqs )
			{
			Name = name;
			Requirements = reqs;
			}
		}
	}
