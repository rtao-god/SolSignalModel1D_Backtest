/*using System;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Api.CurrentPrediction
	{
	/// <summary>
	/// Временная заглушка-источник данных.
	/// Сейчас кидает NotImplementedException.
	/// 
	/// В реальной реализации сюда нужно перенести логику из твоего
	/// консольного Program.Main до момента runner.Run(...):
	/// - обновление свечей;
	/// - расчёт индикаторов;
	/// - BuildDailyRowsAsync;
	/// - CreatePredictionEngineOrFallback;
	/// - LoadPredictionRecordsAsync;
	/// и т.д.
	/// 
	/// В итоге должны получиться:
	/// - список PredictionRecord;
	/// - список 6h свечей SOL.
	/// </summary>
	public sealed class PredictionDataSourceStub : IPredictionDataSource
		{
		public Task<PredictionDataContext> GetLatestContextAsync ()
			{
			// TODO: сюда перенести фактический код построения records/solAll6h
			// из существующего Program.Main.
			throw new NotImplementedException (
				"PredictionDataSourceStub: нужно реализовать загрузку PredictionRecord и SOL 6h.");
			}
		}
	}
*/