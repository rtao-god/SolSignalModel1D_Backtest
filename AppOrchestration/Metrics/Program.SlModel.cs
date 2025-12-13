using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static void RunSlModelOffline (
			List<DataRow> allRows,
			List<BacktestRecord> records,
			List<Candle1h> sol1h,
			List<Candle1m> sol1m,
			List<Candle6h> solAll6h )
			{
			var boundary = new TrainBoundary (_trainUntilUtc, NyTz);
			var split = boundary.Split (allRows, r => r.Date);

			if (split.Excluded.Count > 0)
				{
				var sample = split.Excluded
					.Take (Math.Min (10, split.Excluded.Count))
					.Select (r => r.Date.ToString ("O"));

				throw new InvalidOperationException (
					$"[sl-offline] Found excluded days (baseline-exit undefined). " +
					$"count={split.Excluded.Count}. sample=[{string.Join (", ", sample)}].");
				}

			var slTrainRows = split.Train as List<DataRow> ?? split.Train.ToList ();

			TrainAndApplySlModelOffline (
				allRows: slTrainRows,
				records: records,
				sol1h: sol1h,
				sol1m: sol1m,
				solAll6h: solAll6h
			);
			}
		}
	}
