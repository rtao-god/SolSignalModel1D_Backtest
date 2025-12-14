using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Debug-фасад для тестов: даёт доступ к BootstrapRowsAndCandlesAsync
		/// с тем же tuple, что использует Main.
		/// В прод-логике НЕ используется.
		/// </summary>
		public static Task<(
			List<BacktestRecord> AllRows,
			List<BacktestRecord> Mornings,
			List<Candle6h> SolAll6h,
			List<Candle1h> SolAll1h,
			List<Candle1m> Sol1m)> DebugBootstrapRowsAndCandlesAsync ()
			{
			return BootstrapRowsAndCandlesAsync ();
			}
		}
	}
