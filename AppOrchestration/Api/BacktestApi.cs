using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: публичный энтрипоинт для сборки снапшота бэктеста,
	/// переиспользуемый как консолью, так и API.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Высокоуровневый пайплайн подготовки данных для бэктеста/превью.
		/// 
		/// Логика по слоям:
		/// 1) BootstrapDataAsync — общий инфраструктурный бутстрап:
		///    - обновление свечей;
		///    - загрузка всех временных рядов;
		///    - индикаторы;
		///    - дневные строки (DailyRowsBundle).
		/// 2) Поверх бутстрапа:
		///    - дневная модель (PredictionRecord);
		///    - SL-модель;
		///    - Delayed A по минуткам.
		/// 
		/// На выходе выдаётся стабильный контракт BacktestDataSnapshot,
		/// который использует BacktestEngine и API-превью.
		/// </summary>
		public static async Task<BacktestDataSnapshot> BuildBacktestDataAsync ()
			{
			// 1. Общий бутстрап данных (свечи + индикаторы + дневные строки).
			var bootstrap = await BootstrapDataAsync ();

			var rowsBundle = bootstrap.RowsBundle;
			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// 2. Основная дневная модель: строим prediction-записи по утренним точкам.
			List<BacktestRecord> records;
				{
				var engine = CreatePredictionEngineOrFallback (allRows);

				records = await LoadPredictionRecordsAsync (
					mornings,
					bootstrap.SolAll6h,
					engine
				);

				Console.WriteLine ($"[records] built = {records.Count}");
				}

			// 3. SL-модель: обучаемся строго на TrainOnly<BacktestRecord> по baseline-exit контракту.
				{
				var boundary = new TrainBoundary (_trainUntilUtc, NyTz);

				// Важно: сплит делаем по records, потому что SL-оффлайн-лейблы/фичи привязаны к BacktestRecord.
				var orderedRecords = records
					.OrderBy (r => r.DateUtc)
					.ToList ();

				var recSplit = boundary.SplitStrict (
					items: orderedRecords,
					entryUtcSelector: r => r.DateUtc,
					tag: "sl.records");

				Console.WriteLine (
					$"[sl] records split: train={recSplit.Train.Count}, oos={recSplit.Oos.Count}, trainUntilUtc={_trainUntilUtc:O}");

				// Жёсткое требование: не допускаем "почти пустой" train.
				if (recSplit.Train.Count < 50)
					{
					var trMin = recSplit.Train.Count > 0 ? recSplit.Train.Min (r => r.DateUtc) : default;
					var trMax = recSplit.Train.Count > 0 ? recSplit.Train.Max (r => r.DateUtc) : default;

					throw new InvalidOperationException (
						$"[sl] SL train subset too small (count={recSplit.Train.Count}). " +
						$"period={(recSplit.Train.Count > 0 ? $"{trMin:yyyy-MM-dd}..{trMax:yyyy-MM-dd}" : "n/a")}. " +
						"Adjust trainUntilUtc / history coverage so SL has enough train records.");
					}

				TrainAndApplySlModelOffline (
					trainRecords: recSplit.Train,
					records: records,
					sol1h: bootstrap.SolAll1h,
					sol1m: bootstrap.Sol1m,
					solAll6h: bootstrap.SolAll6h
				);
				}

			// 4. Delayed A: расчёт отложенной доходности по минутным свечам.
				{
				PopulateDelayedA (
					records: records,
					allRows: allRows,
					sol1h: bootstrap.SolAll1h,
					solAll6h: bootstrap.SolAll6h,
					sol1m: bootstrap.Sol1m,
					dipFrac: 0.005,
					tpPct: 0.010,
					slPct: 0.010
				);
				}

			// 5. Финальный снэпшот.
			return new BacktestDataSnapshot
				{
				Mornings = mornings,
				Records = records,
				Candles1m = bootstrap.Sol1m
				};
			}
		}
	}
