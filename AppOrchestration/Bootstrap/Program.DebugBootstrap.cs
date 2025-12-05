using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Debug-фасад для тестов: даёт доступ к BootstrapRowsAndCandlesAsync
		/// с тем же tuple, что использует Main.
		/// В прод-логике не используется.
		/// </summary>
		public static Task<(
			List<DataRow> AllRows,
			List<DataRow> Mornings,
			List<Candle6h> SolAll6h,
			List<Candle1h> SolAll1h,
			List<Candle1m> Sol1m)> DebugBootstrapRowsAndCandlesAsync ()
			{
			return BootstrapRowsAndCandlesAsync ();
			}
		}
	}
