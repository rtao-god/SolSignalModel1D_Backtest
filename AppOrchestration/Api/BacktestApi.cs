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
			// Методы, которые дергает BootstrapDataAsync (UpdateCandlesAsync, LoadAllCandlesAndWindow,
			// BuildIndicatorsAsync, BuildDailyRowsBundleAsync), остаются инкапсулированы внутри Program.
			var bootstrap = await BootstrapDataAsync ();

			var rowsBundle = bootstrap.RowsBundle;
			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// 2. Основная дневная модель: строим prediction-записи по утренним точкам.
			// Здесь переиспользуются уже существующие утилиты Program:
			// - CreatePredictionEngineOrFallback;
			// - LoadPredictionRecordsAsync.
			// Это позволяет менять реализацию модели без влияния на API-контракт.
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

			// 3. SL-модель: оффлайн-обучение и применение на основе дневных предсказаний.
			// Обучение ограничивается train-окном (_trainUntilUtc), чтобы не ловить "look-ahead bias".
				{
				var boundary = new TrainBoundary (_trainUntilUtc, NyTz);
				var split = boundary.Split (allRows, r => r.Causal.DateUtc);

				var slTrainRows = split.Train;

				if (split.Excluded.Count > 0)
					{
					Console.WriteLine (
						$"[sl] WARNING: excluded={split.Excluded.Count} rows (baseline-exit undefined). Они не участвуют в обучении SL.");
					}

				TrainAndApplySlModelOffline (
					allRows: slTrainRows,
					records: records,
					sol1h: bootstrap.SolAll1h,
					sol1m: bootstrap.Sol1m,
					solAll6h: bootstrap.SolAll6h
				);
				}

			// 4. Delayed A: расчёт отложенной доходности по минутным свечам.
			// Параметры dipFrac/tpPct/slPct зашиты здесь, чтобы контракт снапшота оставался простым,
			// а детали risk-профиля были локализованы.
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

			// 5. Финальный снэпшот:
			// минимально необходимый набор данных для бэктеста и превью.
			return new BacktestDataSnapshot
				{
				Mornings = mornings,
				Records = records,
				Candles1m = bootstrap.Sol1m
				};
			}
		}
	}
