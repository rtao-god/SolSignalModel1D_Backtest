using SolSignalModel1D_Backtest.Core.Infra.Perf;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: агрегирующий бутстрап приложения.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Инфраструктурный бутстрап всего пайплайна данных.
		/// Оборачивает:
		/// 1) UpdateCandlesAsync;
		/// 2) LoadAllCandlesAndWindow;
		/// 3) BuildIndicatorsAsync;
		/// 4) BuildDailyRowsAsync.
		/// Возвращает единый контейнер BootstrapData вместо out-параметров.
		/// </summary>
		private static async Task<BootstrapData> BootstrapDataAsync ()
			{
			// HttpClient используется только здесь и корректно утилизируется.
			// Это избегает глобального статика и даёт явный контроль над временем жизни.
			using var http = new HttpClient ();

			// --- 1. Обновление свечей (сетевой блок) ---
			// Обновляем историю по SOL/BTC/PAXG (полный бэкофилл или догонка хвостов).
			await PerfLogging.MeasureAsync (
				"UpdateCandlesAsync",
				() => UpdateCandlesAsync (http)
			);

			// --- 2. Загрузка всех таймфреймов и окна бэктеста ---
			// Здесь формируется единый набор рядов (6h/1h/1m) и окно [fromUtc, toUtc].
			LoadAllCandlesAndWindow (
				out var solAll6h,
				out var btcAll6h,
				out var paxgAll6h,
				out var solAll1h,
				out var sol1m,
				out var fromUtc,
				out var toUtc
			);

			// --- 3. Индикаторы ---
			// Строим/обновляем дневные индикаторы в расширенном окне,
			// чтобы на границе fromUtc не было "сырых" значений.
			var indicators = await PerfLogging.MeasureAsync (
				"BuildIndicatorsAsync",
				() => BuildIndicatorsAsync (http, fromUtc, toUtc)
			);

			// --- 4. Дневные строки (allRows + mornings) ---
			// Единственный источник правды для построения allRows/mornings — BuildDailyRowsAsync.
			// Здесь же логируется гистограмма по NY-часам и базовая диагностика.
			var rowsBundle = await PerfLogging.MeasureAsync (
				"BuildDailyRowsAsync",
				() => BuildDailyRowsAsync (
					indicators,
					fromUtc,
					toUtc,
					solAll6h,
					btcAll6h,
					paxgAll6h,
					sol1m
				)
			);

			// Собираем всё в один объект, чтобы не плодить out/ref и не разносить связанное состояние.
			return new BootstrapData
				{
				SolAll6h = solAll6h,
				BtcAll6h = btcAll6h,
				PaxgAll6h = paxgAll6h,
				SolAll1h = solAll1h,
				Sol1m = sol1m,
				FromUtc = fromUtc,
				ToUtc = toUtc,
				RowsBundle = rowsBundle
				};
			}
		}
	}
