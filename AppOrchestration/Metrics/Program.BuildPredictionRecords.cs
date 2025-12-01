using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Строит записи прогнозов дневной модели по утренним точкам.
		/// Здесь инкапсулируется выбор PredictionEngine и вычисление forward-метрик.
		/// </summary>
		private static async Task<List<PredictionRecord>> BuildPredictionRecordsAsync (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h
		)
			{
			// PredictionEngine создаётся один раз для всей дневной выборки.
			var engine = CreatePredictionEngineOrFallback (allRows);

			// Загрузка forward-метрик по утренним точкам на основе 6h-свечей.
			var records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);
			DumpDailyPredHistograms (records, _trainUntilUtc);

			Console.WriteLine ($"[records] built = {records.Count}");

			return records;
			}
		}
	}
