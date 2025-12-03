using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Infra.Perf;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: точка входа и верхнеуровневый пайплайн.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Флажок: гонять ли self-check'и при старте приложения.
		/// При false поведение полностью совпадает с текущим.
		/// </summary>
		private static readonly bool RunSelfChecksOnStartup = true;

		/// <summary>
		/// Глобальная таймзона Нью-Йорка для всех расчётов.
		/// </summary>
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public static async Task Main ( string[] args )
			{
			// Старт общего таймера приложения как можно раньше:
			// всё, что ниже, попадёт в "фактическое время работы" в PerfLogging.
			PerfLogging.StartApp ();

			// Логируем инфо о среде — помогает отслеживать странные баги.
			Console.WriteLine ($".NET runtime: {Environment.Version}, 64bit={Environment.Is64BitProcess}");

			// --- 1. Бутстрап свечей и дневных строк ---
			// Внешний PerfLogging.MeasureAsync — для общей Σ времени,
			// внутренний PerfBlockLogger.MeasureAsync — для старых [perf]-логов.
			var (allRows, mornings, solAll6h, solAll1h, sol1m) =
				await PerfLogging.MeasureAsync<
					(List<DataRow> AllRows,
						List<DataRow> Mornings,
						List<Candle6h> SolAll6h,
						List<Candle1h> SolAll1h,
						List<Candle1m> Sol1m)> (
					"(top) BootstrapRowsAndCandlesAsync",
					() => PerfBlockLogger.MeasureAsync (
						"(top) BootstrapRowsAndCandlesAsync",
						() => BootstrapRowsAndCandlesAsync ()
					)
				);

			// --- 2. Дневная модель: PredictionEngine + forward-метрики ---
			var records = await PerfLogging.MeasureAsync (
				"BuildPredictionRecordsAsync",
				() => PerfBlockLogger.MeasureAsync (
					"BuildPredictionRecordsAsync",
					() => BuildPredictionRecordsAsync (allRows, mornings, solAll6h)
				)
			);

			// --- 3. SL-модель (офлайн) поверх дневных предсказаний ---
			PerfLogging.Measure (
				"RunSlModelOffline",
				() => PerfBlockLogger.Measure (
					"RunSlModelOffline",
					() => RunSlModelOffline (allRows, records, solAll1h, sol1m, solAll6h)
				)
			);

			// Флаг, идёт ли дальше основной пайплайн после self-check'ов.
			var pipelineShouldContinue = true;

			// --- 4. Self-checks (по флажку) ---
			if (RunSelfChecksOnStartup)
				{
				var selfCheckContext = new SelfCheckContext
					{
					AllRows = allRows,
					Mornings = mornings,
					Records = records,
					SolAll6h = solAll6h,
					SolAll1h = solAll1h,
					Sol1m = sol1m,
					TrainUntilUtc = _trainUntilUtc,
					NyTz = NyTz
					};

				var selfCheckResult = await PerfLogging.MeasureAsync (
					"SelfCheckRunner.RunAsync",
					() => PerfBlockLogger.MeasureAsync (
						"SelfCheckRunner.RunAsync",
						() => SelfCheckRunner.RunAsync (selfCheckContext)
					)
				);

				Console.WriteLine ($"[self-check] Success = {selfCheckResult.Success}");
				if (selfCheckResult.Warnings.Count > 0)
					{
					Console.WriteLine ("[self-check] warnings:");
					foreach (var w in selfCheckResult.Warnings)
						Console.WriteLine ("  - " + w);
					}

				if (selfCheckResult.Errors.Count > 0)
					{
					Console.WriteLine ("[self-check] errors:");
					foreach (var e in selfCheckResult.Errors)
						Console.WriteLine ("  - " + e);
					}

				if (!selfCheckResult.Success)
					{
					Console.WriteLine ("[self-check] FAIL → основная часть пайплайна не выполняется.");
					// Поведение пайплайна то же (дальше не идём),
					// но даём PerfLogging возможность честно завершить таймер и вывести сводку.
					pipelineShouldContinue = false;
					}
				}

			if (pipelineShouldContinue)
				{
				// --- 5. Бэктест-профили ---
				await PerfLogging.MeasureAsync (
					"EnsureBacktestProfilesInitializedAsync",
					() => PerfBlockLogger.MeasureAsync (
						"EnsureBacktestProfilesInitializedAsync",
						() => EnsureBacktestProfilesInitializedAsync ()
					)
				);

				// --- 6. Бэктест + отчёты ---
				PerfLogging.Measure (
					"RunBacktestAndReports",
					() => PerfBlockLogger.Measure (
						"RunBacktestAndReports",
						() => RunBacktestAndReports (mornings, records, sol1m)
					)
				);
				}

			// В конце всегда печатаем сводку по времени и поджимаем скролл консоли.
			PerfLogging.StopAppAndPrintSummary ();
			}

		private static void DumpDailyPredHistograms ( List<PredictionRecord> records, DateTime trainUntilUtc )
			{
			if (records == null || records.Count == 0)
				return;

			var train = records.Where (r => r.DateUtc <= trainUntilUtc).ToList ();
			var oos = records.Where (r => r.DateUtc > trainUntilUtc).ToList ();

			static string Hist ( IEnumerable<int> xs )
				{
				return string.Join (", ",
					xs.GroupBy (v => v)
					  .OrderBy (g => g.Key)
					  .Select (g => $"{g.Key}={g.Count ()}"));
				}

			Console.WriteLine ($"[daily] train size = {train.Count}, oos size = {oos.Count}");

			Console.WriteLine ("[daily] train TrueLabel hist: " + Hist (train.Select (r => r.TrueLabel)));
			Console.WriteLine ("[daily] train PredLabel hist: " + Hist (train.Select (r => r.PredLabel)));

			if (oos.Count > 0)
				{
				Console.WriteLine ("[daily] oos TrueLabel hist: " + Hist (oos.Select (r => r.TrueLabel)));
				Console.WriteLine ("[daily] oos PredLabel hist: " + Hist (oos.Select (r => r.PredLabel)));
				}
			}
		}
	}
