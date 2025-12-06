using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Utils;
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
			// Централизованное разбиение на train/OOS, чтобы везде использовать
			// один и тот же критерий Date <= _trainUntilUtc.
			var (slTrainRows, _) = TrainOosSplitHelper.SplitByTrainBoundary (allRows, _trainUntilUtc);

			// Основная логика SL-модели остаётся в отдельном методе, сюда вынесен только «проводящий» код.
			// На вход TrainAndApplySlModelOffline передаётся уже train-сабсет.
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
