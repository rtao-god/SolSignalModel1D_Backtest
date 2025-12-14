using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static async Task<(List<BacktestRecord> AllRows,
				List<BacktestRecord> Mornings,
				List<Candle6h> SolAll6h,
				List<Candle1h> SolAll1h,
				List<Candle1m> Sol1m)> BootstrapRowsAndCandlesAsync ()
			{
			var bootstrap = await BootstrapDataAsync ();

			var solAll6h = bootstrap.SolAll6h;
			var solAll1h = bootstrap.SolAll1h;
			var sol1m = bootstrap.Sol1m;

			var rowsBundle = bootstrap.RowsBundle;
			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			// Контракт: на бутстрапе все временные ряды уже отсортированы и уникальны.
			SeriesGuards.EnsureStrictlyAscendingUtc (solAll6h, c => c.OpenTimeUtc, "bootstrap.solAll6h");
			SeriesGuards.EnsureStrictlyAscendingUtc (solAll1h, c => c.OpenTimeUtc, "bootstrap.solAll1h");
			SeriesGuards.EnsureStrictlyAscendingUtc (sol1m, c => c.OpenTimeUtc, "bootstrap.sol1m");

			SeriesGuards.EnsureStrictlyAscendingUtc (allRows, r => r.Causal.DateUtc, "bootstrap.allRows");
			SeriesGuards.EnsureStrictlyAscendingUtc (mornings, r => r.Causal.DateUtc, "bootstrap.mornings");

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			return (allRows, mornings, solAll6h, solAll1h, sol1m);
			}
		}
	}
