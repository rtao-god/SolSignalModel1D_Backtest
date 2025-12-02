using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Обучает и применяет SL-модель в offline-режиме.
		/// Отделяет выборку обучения по _trainUntilUtc и делегирует доменной функции TrainAndApplySlModelOffline.
		/// </summary>
		private static void RunSlModelOffline (
			List<DataRow> allRows,
			List<PredictionRecord> records,
			List<Candle1h> sol1h,
			List<Candle1m> sol1m,
			List<Candle6h> solAll6h
		)
			{
			var slTrainRows = allRows
				.Where (r => r.Date <= _trainUntilUtc)
				.ToList ();

			// Основная логика SL-модели остаётся в отдельном методе, сюда вынесен только «проводящий» код.
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
