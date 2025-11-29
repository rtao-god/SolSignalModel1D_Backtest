using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public static async Task Main ( string[] args )
			{
			var (allRows, mornings, solAll6h, solAll1h, sol1m) =
				await BootstrapRowsAndCandlesAsync ();

			// === МОДЕЛЬ (микро-слой + дневная схема через PredictionEngine) ===

			// --- 5. Предсказания и forward-метрики ---
			var records = await BuildPredictionRecordsAsync (allRows, mornings, solAll6h);

			// --- 5a. PFI для дневных моделей ---
			//RunDailyPfi (allRows);

			// --- 6. SL-модель ---
			//RunSlModelOffline (allRows, records, solAll1h, sol1m, solAll6h);

			// --- 7. Delayed A по минуткам ---
			/*	PopulateDelayedAForRecords (
					records: records,
					allRows: allRows,
					sol1h: solAll1h,
					solAll6h: solAll6h,
					sol1m: sol1m
				);*/

			// --- 8. Бэктест + сохранение отчётов ---
		/*	await EnsureBacktestProfilesInitializedAsync ();

			RunBacktestAndReports (
				mornings: mornings,
				records: records,
				sol1m: sol1m
			);*/

			// --- 9. Сценарные стратегии по дневной модели ---
			RunStrategyScenarios (
				mornings: mornings,
				records: records,
				sol1m: sol1m
			);
			}
		}
	}
