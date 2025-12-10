using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Строит записи прогнозов дневной модели по утренним точкам.
		/// Здесь инкапсулируется выбор PredictionEngine и вычисление forward-метрик.
		/// </summary>
		private static async Task<List<BacktestRecord>> BuildPredictionRecordsAsync (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h
		)
			{
			// Локальный хелпер для логирования диапазона дат и day-of-week.
			static void DumpRange<T> ( string label, IReadOnlyList<T> items, Func<T, DateTime> selector )
				{
				if (items == null || items.Count == 0)
					{
					Console.WriteLine ($"[{label}] empty");
					return;
					}

				var dates = items.Select (selector).ToList ();
				var min = dates.Min ();
				var max = dates.Max ();

				Console.WriteLine (
					$"[{label}] range = [{min:yyyy-MM-dd} ({min.DayOfWeek}); {max:yyyy-MM-dd} ({max.DayOfWeek})], count={items.Count}");

				var dowHist = dates
					.GroupBy (d => d.DayOfWeek)
					.OrderBy (g => g.Key)
					.Select (g => $"{g.Key}={g.Count ()}")
					.ToArray ();

				Console.WriteLine ($"[{label}] DayOfWeek hist: {string.Join (", ", dowHist)}");
				}

			// Логируем, какие вообще дни есть в утренних точках.
			DumpRange ("mornings", mornings, r => r.Date);

			// PredictionEngine создаётся один раз для всей дневной выборки.
			var engine = CreatePredictionEngineOrFallback (allRows);

			// Загрузка forward-метрик по утренним точкам на основе 6h-свечей.
			var records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);

			// Логируем, какие дни в итоге попали в records.
			DumpRange ("records", records, r => r.DateUtc);

			DumpDailyPredHistograms (records, _trainUntilUtc);

			Console.WriteLine ($"[records] built = {records.Count}");

			return records;
			}
		}
	}
