using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Infra.Perf;
using SolSignalModel1D_Backtest.Diagnostics.PnL;
using SolSignalModel1D_Backtest.Diagnostics;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest
{
    /// <summary>
    /// Частичный класс Program: точка входа и верхнеуровневый пайплайн.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Флажок: гонять ли self-check'и при старте приложения, которые могут заблокировать основной пайплайн при падении.
        /// </summary>
        private static readonly bool RunSelfChecksOnStartup = true;

        /// <summary>
        /// Глобальная таймзона Нью-Йорка для всех расчётов.
        /// </summary>
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        public static async Task Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--scan-gaps-1m", StringComparison.OrdinalIgnoreCase)))
            {
                await RunBinance1mGapScanAsync();
                return;
            }

            if (args.Any(a => string.Equals(a, "--scan-gaps-1h", StringComparison.OrdinalIgnoreCase)))
            {
                await RunBinance1hGapScanAsync();
                return;
            }

            if (args.Any(a => string.Equals(a, "--scan-gaps-6h", StringComparison.OrdinalIgnoreCase)))
            {
                await RunBinance6hGapScanAsync();
                return;
            }

            PerfLogging.StartApp();

            try
            {
                var (allRows, mornings, solAll6h, solAll1h, sol1m) =
                    await PerfLogging.MeasureAsync(
                        "(top) BootstrapRowsAndCandlesAsync",
                        () =>
                            PerfBlockLogger.MeasureAsync(
                                "(top) BootstrapRowsAndCandlesAsync",
                                () => BootstrapRowsAndCandlesAsync()
                            )
                    );

                var records = await PerfLogging.MeasureAsync(
                    "BuildPredictionRecordsAsync",
                    () => PerfBlockLogger.MeasureAsync(
                        "BuildPredictionRecordsAsync",
                        () => BuildPredictionRecordsAsync(allRows, mornings, sol1m)
                    )
                );

                DumpDailyAccuracyWithDatasetSplit(allRows, records, _trainUntilExitDayKeyUtc);

                RuntimeLeakageDebug.PrintDailyModelTrainOosProbe(
                    records,
                    _trainUntilExitDayKeyUtc,
                    NyTz,
                    boundarySampleCount: 2,
                    allRows: allRows
                );

                DailyPnlProbe.RunSimpleProbe(records, _trainUntilExitDayKeyUtc, NyTz);

                RunDailyPfi(allRows);

                PerfLogging.Measure(
                    "RunSlModelOffline",
                    () => PerfBlockLogger.Measure(
                        "RunSlModelOffline",
                        () => RunSlModelOffline(allRows, records, solAll1h, sol1m, solAll6h)
                    )
                );

                var pipelineShouldContinue = true;

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
                        TrainUntilExitDayKeyUtc = _trainUntilExitDayKeyUtc,
                        NyTz = NyTz
                    };

                    var selfCheckResult = await PerfLogging.MeasureAsync(
                        "SelfCheckRunner.RunAsync",
                        () => PerfBlockLogger.MeasureAsync(
                            "SelfCheckRunner.RunAsync",
                            () => SelfCheckRunner.RunAsync(selfCheckContext)
                        )
                    );

                    Console.WriteLine($"[self-check] Success = {selfCheckResult.Success}");

                    if (selfCheckResult.Warnings.Count > 0)
                    {
                        Console.WriteLine("[self-check] warnings:");
                        foreach (var w in selfCheckResult.Warnings)
                            Console.WriteLine("  - " + w);
                    }

                    if (selfCheckResult.Errors.Count > 0)
                    {
                        Console.WriteLine("[self-check] errors:");
                        foreach (var e in selfCheckResult.Errors)
                            Console.WriteLine("  - " + e);
                    }

                    if (!selfCheckResult.Success)
                    {
                        Console.WriteLine("[self-check] FAIL → основная часть пайплайна не выполняется.");
                        pipelineShouldContinue = false;
                    }
                }

                if (pipelineShouldContinue)
                {
                    await PerfLogging.MeasureAsync(
                        "EnsureBacktestProfilesInitializedAsync",
                        () => PerfBlockLogger.MeasureAsync(
                            "EnsureBacktestProfilesInitializedAsync",
                            () => EnsureBacktestProfilesInitializedAsync()
                        )
                    );

                    PerfLogging.Measure(
                        "RunBacktestAndReports",
                        () => PerfBlockLogger.Measure(
                            "RunBacktestAndReports",
                            () => RunBacktestAndReports(mornings, records, sol1m)
                        )
                    );
                }
            }
            finally
            {
                PerfLogging.StopAppAndPrintSummary();
            }
        }

        private static void DumpDailyAccuracyWithDatasetSplit(
            List<LabeledCausalRow> allRows,
            List<BacktestRecord> records,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            var dataset = DailyDatasetBuilder.Build(
                allRows,
                trainUntilExitDayKeyUtc,
                balanceMove: false,
                balanceDir: true,
                balanceTargetFrac: 0.70,
                dayKeysToExclude: null
            );

            var trainDates = new HashSet<DateTime>(dataset.TrainRows.Select(r => r.EntryDayKeyUtc.Value));

            var trainRecords = new List<BacktestRecord>(trainDates.Count);
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];

                var recDayKey = r.EntryDayKeyUtc.Value;

                if (trainDates.Contains(recDayKey))
                    trainRecords.Add(r);
            }

            SplitByTrainUntilUtc(records, trainUntilExitDayKeyUtc, out _, out var oosRecords);

            static double Acc(IReadOnlyList<BacktestRecord> xs)
            {
                if (xs == null) throw new ArgumentNullException(nameof(xs));
                if (xs.Count == 0) return 0.0;

                int ok = 0;
                for (int i = 0; i < xs.Count; i++)
                {
                    var r = xs[i];
                    if (r.PredLabel_Total == r.TrueLabel)
                        ok++;
                }

                return (double)ok / xs.Count;
            }

            var trainAcc = Acc(trainRecords);
            var oosAcc = Acc(oosRecords);

            Console.WriteLine($"[daily-acc] trainAcc(in-sample, dataset-based) = {trainAcc:0.000}");
            Console.WriteLine($"[daily-acc] oosAcc(out-of-sample, date-based)  = {oosAcc:0.000}");
        }

        private static void SplitByTrainUntilUtc(
            IReadOnlyList<BacktestRecord> records,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            out List<BacktestRecord> train,
            out List<BacktestRecord> oos)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            var ordered = records
                .OrderBy(static r => r.EntryUtc.Value) // ВАЖНО: НЕ r.Causal.EntryUtc
                .ToList();

            Console.WriteLine(
                $"[split] запуск SplitByBaselineExitStrict: тег='train-split.records', trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

            var split = NyTrainSplit.SplitByBaselineExitStrict<BacktestRecord>(
                ordered: ordered,
                entrySelector: static r => r.EntryUtc,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: NyTz,
                tag: "train-split.records");

            train = split.Train.ToList();
            oos = split.Oos.ToList();
        }
    }
}
