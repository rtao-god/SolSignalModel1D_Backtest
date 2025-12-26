using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Infra.Perf;
using SolSignalModel1D_Backtest.Core.ML.Diagnostics.PnL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Diagnostics;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
{
    /// <summary>
    /// Частичный класс Program: точка входа и верхнеуровневый пайплайн.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Флажок: гонять ли self-check'и при старте приложения, которые могут заблокировать основной пайплайн.
        /// </summary>
        private static readonly bool RunSelfChecksOnStartup = false;

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
                        () => BuildPredictionRecordsAsync(allRows, mornings, solAll6h)
                    )
                );

                DumpDailyAccuracyWithDatasetSplit(allRows, records, _trainUntilUtc);

                RuntimeLeakageDebug.PrintDailyModelTrainOosProbe(
                    records,
                    new TrainUntilUtc(_trainUntilUtc),
                    NyTz,
                    boundarySampleCount: 2
                );

                DailyPnlProbe.RunSimpleProbe(records, _trainUntilUtc, NyTz);

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
                        TrainUntilUtc = _trainUntilUtc,
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

        private static void DumpDailyPredHistograms(List<BacktestRecord> records, DateTime trainUntilUtc)
        {
            if (records == null || records.Count == 0)
                return;

            SplitByTrainUntilUtc(records, trainUntilUtc, out var train, out var oos);

            static string Hist(IEnumerable<int> xs)
            {
                return string.Join(", ",
                    xs.GroupBy(v => v)
                      .OrderBy(g => g.Key)
                      .Select(g => $"{g.Key}={g.Count()}"));
            }

            Console.WriteLine($"[daily] train size = {train.Count}, oos size = {oos.Count}");

            Console.WriteLine("[daily] train TrueLabel hist: " + Hist(train.Select(r => r.TrueLabel)));
            Console.WriteLine("[daily] train PredLabel hist: " + Hist(train.Select(r => r.PredLabel)));

            if (oos.Count > 0)
            {
                Console.WriteLine("[daily] oos TrueLabel hist: " + Hist(oos.Select(r => r.TrueLabel)));
                Console.WriteLine("[daily] oos PredLabel hist: " + Hist(oos.Select(r => r.PredLabel)));
            }
        }

        private static void DumpDailyAccuracyWithDatasetSplit(
            List<LabeledCausalRow> allRows,
            List<BacktestRecord> records,
            DateTime trainUntilUtc)
        {
            var trainUntilExitDayKeyUtc = ExitDayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc);

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

            SplitByTrainUntilUtc(records, trainUntilUtc, out _, out var oosRecords);

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

            Console.WriteLine($"[daily-acc] trainAcc(dataset-based) = {trainAcc:0.000}");
            Console.WriteLine($"[daily-acc] oosAcc(date-based)      = {oosAcc:0.000}");
        }

        private static void SplitByTrainUntilUtc(
            IReadOnlyList<BacktestRecord> records,
            DateTime trainUntilUtc,
            out List<BacktestRecord> train,
            out List<BacktestRecord> oos)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (trainUntilUtc == default)
                throw new ArgumentException("trainUntilUtc must be initialized (non-default).", nameof(trainUntilUtc));
            if (trainUntilUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof(trainUntilUtc));

            var trainUntilExitDayKeyUtc = ExitDayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc);

            var ordered = records
                .OrderBy(static r => r.EntryUtc.Value) // ВАЖНО: НЕ r.Causal.EntryUtc
                .ToList();

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
