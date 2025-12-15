using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Infra.Perf;
using SolSignalModel1D_Backtest.Core.ML.Diagnostics.PnL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Diagnostics;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;

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
			// Ранние утилитарные команды не должны стартовать общий perf-таймер.
			if (args.Any (a => string.Equals (a, "--scan-gaps-1m", StringComparison.OrdinalIgnoreCase)))
				{
				await RunBinance1mGapScanAsync ();
				return;
				}

			if (args.Any (a => string.Equals (a, "--scan-gaps-1h", StringComparison.OrdinalIgnoreCase)))
				{
				await RunBinance1hGapScanAsync ();
				return;
				}

			if (args.Any (a => string.Equals (a, "--scan-gaps-6h", StringComparison.OrdinalIgnoreCase)))
				{
				await RunBinance6hGapScanAsync ();
				return;
				}

			PerfLogging.StartApp ();

			try
				{
				// --- 1. Бутстрап свечей и дневных строк ---
				// Внешний PerfLogging.MeasureAsync — для общей Σ времени,
				// внутренний PerfBlockLogger.MeasureAsync — для старых [perf]-логов.
				var (allRows, mornings, solAll6h, solAll1h, sol1m) =
					await PerfLogging.MeasureAsync (
						"(top) BootstrapRowsAndCandlesAsync",
						() =>
							PerfBlockLogger.MeasureAsync (
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

				// Честная train-accuracy по тем строкам, которые реально попали в train-датасет.
				DumpDailyAccuracyWithDatasetSplit (allRows, records, _trainUntilUtc);

				// Консольная проверка разделения train/OOS и accuracy дневной модели на реальных данных.
				// FIX: добавлен nyTz (CS7036).
				RuntimeLeakageDebug.PrintDailyModelTrainOosProbe (
					records,
					_trainUntilUtc,
					NyTz,
					boundarySampleCount: 2
				);

				// FIX: добавлен nyTz (CS7036).
				DailyPnlProbe.RunSimpleProbe (records, _trainUntilUtc, NyTz);

				RunDailyPfi (allRows);

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
						// Не глотаем ошибки: просто не продолжаем основной пайплайн.
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
				}
			finally
				{
				// В конце всегда печатаем сводку по времени.
				PerfLogging.StopAppAndPrintSummary ();
				}
			}

		private static void DumpDailyPredHistograms ( List<BacktestRecord> records, DateTime trainUntilUtc )
			{
			if (records == null || records.Count == 0)
				return;

			SplitByTrainUntilUtc (records, trainUntilUtc, out var train, out var oos);

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

		private static void DumpDailyAccuracyWithDatasetSplit (
			List<BacktestRecord> allRows,
			List<BacktestRecord> records,
			DateTime trainUntilUtc )
			{
			// Собираем датасет так же, как внутри ModelTrainer.
			// Важно: эта метрика отвечает на вопрос "как модель выглядит на том train, на котором она реально училась".
			var dataset = DailyDatasetBuilder.Build (
				allRows: allRows,
				trainUntilUtc: trainUntilUtc,
				balanceMove: false,
				balanceDir: true,
				balanceTargetFrac: 0.70,
				datesToExclude: null
			);

			var trainDates = new HashSet<DateTime> (dataset.TrainRows.Select (r => r.ToCausalDateUtc()));

			var trainRecords = new List<BacktestRecord> (trainDates.Count);
			foreach (var r in records)
				{
				if (trainDates.Contains (r.ToCausalDateUtc()))
					trainRecords.Add (r);
				}

			SplitByTrainUntilUtc (records, trainUntilUtc, out _, out var oosRecords);

			static double Acc ( IReadOnlyList<BacktestRecord> xs )
				{
				if (xs == null) throw new ArgumentNullException (nameof (xs));
				if (xs.Count == 0) return 0.0;

				int ok = 0;
				for (int i = 0; i < xs.Count; i++)
					{
					var r = xs[i];
					if (r.PredLabel_Total == r.TrueLabel)
						ok++;
					}

				return (double) ok / xs.Count;
				}

			var trainAcc = Acc (trainRecords);
			var oosAcc = Acc (oosRecords);

			Console.WriteLine ($"[daily-acc] trainAcc(dataset-based) = {trainAcc:0.000}");
			Console.WriteLine ($"[daily-acc] oosAcc(date-based)      = {oosAcc:0.000}");
			}

		private static void SplitByTrainUntilUtc (
			IReadOnlyList<BacktestRecord> records,
			DateTime trainUntilUtc,
			out List<BacktestRecord> train,
			out List<BacktestRecord> oos )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			// Единый контракт сплита как в DailyDatasetBuilder/MicroDatasetBuilder:
			// граница интерпретируется через baseline-exit (TrainBoundary).
			var boundary = new TrainBoundary (trainUntilUtc, NyTz);
			var split = boundary.Split (records, r => r.ToCausalDateUtc());

			if (split.Excluded.Count > 0)
				{
				var sample = split.Excluded
					.Take (Math.Min (10, split.Excluded.Count))
					.Select (r => r.ToCausalDateUtc().ToString ("O"));

				throw new InvalidOperationException (
					$"[train-split] Found excluded records (baseline-exit undefined). " +
					$"count={split.Excluded.Count}. sample=[{string.Join (", ", sample)}].");
				}

			// Возвращаем конкретные List, чтобы внешний код не зависел от внутреннего типа коллекций split.
			train = split.Train is List<BacktestRecord> tl ? tl : split.Train.ToList ();
			oos = split.Oos is List<BacktestRecord> ol ? ol : split.Oos.ToList ();
			}
		}
	}
