using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Заполняет delayed-A слой по минуткам для уже посчитанных records.
		/// </summary>
		private static void PopulateDelayedAForRecords (
			List<BacktestRecord> records,
			List<LabeledCausalRow> allRows,
			List<Candle1h> sol1h,
			List<Candle6h> solAll6h,
			List<Candle1m> sol1m
		)
			{
			PopulateDelayedA (
				records: records,
				allRows: allRows,
				trainUntilExitDayKeyUtc: _trainUntilExitDayKeyUtc,
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
