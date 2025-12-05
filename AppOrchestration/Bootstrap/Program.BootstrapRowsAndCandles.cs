using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Высокоуровневый бутстрап:
		/// - гоняет полный BootstrapDataAsync (HttpClient, свечи, индикаторы, дневные строки);
		/// - достаёт allRows/mornings и нужные ряды по SOL;
		/// - логирует число утренних точек и валидирует, что оно > 0.
		/// На выходе отдаёт только то, что реально нужно моделям/бэктесту.
		/// </summary>
		private static async Task<(List<DataRow> AllRows,
				List<DataRow> Mornings,
				List<Candle6h> SolAll6h,
				List<Candle1h> SolAll1h,
				List<Candle1m> Sol1m)> BootstrapRowsAndCandlesAsync ()
			{
			// Внутри: HttpClient, обновление свечей, индикаторы, построение дневных строк.
			var bootstrap = await BootstrapDataAsync ();

			var solAll6h = bootstrap.SolAll6h;
			var solAll1h = bootstrap.SolAll1h;
			var sol1m = bootstrap.Sol1m;

			var rowsBundle = bootstrap.RowsBundle;
			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			return (allRows, mornings, solAll6h, solAll1h, sol1m);
			}
		}
	}