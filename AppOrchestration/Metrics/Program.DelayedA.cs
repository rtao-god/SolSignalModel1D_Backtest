using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using System.Collections.Generic;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Заполняет delayed-A слой по минуткам для уже посчитанных records.
		/// Константы dipFrac/tpPct/slPct оставлены в том же виде, чтобы не менять поведение.
		/// </summary>
		private static void PopulateDelayedAForRecords (
			List<BacktestRecord> records,
			List<DataRow> allRows,
			List<Candle1h> sol1h,
			List<Candle6h> solAll6h,
			List<Candle1m> sol1m
		)
			{
			PopulateDelayedA (
				records: records,
				allRows: allRows,
				sol1h: sol1h,
				solAll6h: solAll6h,
				sol1m: sol1m,
				dipFrac: 0.005,
				tpPct: 0.010,
				slPct: 0.010
			);
			}
		}
	}
