using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Diagnostics;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
{
    /// <summary>
    /// Печать «модельных» статистик по дневной схеме/SL-модели в разрезе сегментов:
    /// - Train (exit-day-key <= trainUntilExitDayKey);
    /// - OOS (exit-day-key > trainUntilExitDayKey);
    /// - Recent (последние N дней);
    /// - Full history.
    /// </summary>
    public static class BacktestModelStatsPrinter
    {
        [Flags]
        public enum ModelComponentPrintFlags
        {
            None = 0,
            LayerTriClass = 1 << 0,
            MoveModel = 1 << 1,
            DirModel = 1 << 2,
            MicroModel = 1 << 3,
            All = LayerTriClass | MoveModel | DirModel | MicroModel
        }

        public static void Print(
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (sol1m == null) throw new ArgumentNullException(nameof(sol1m));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (trainUntilExitDayKeyUtc.IsDefault) throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            const int RecentDays = 240;
            const int DebugLastDays = 10;

            var snapshot = BacktestDiagnosticsSnapshotBuilder.Build(
                records: records,
                sol1m: sol1m,
                dailyTpPct: dailyTpPct,
                dailySlPct: dailySlPct,
                nyTz: nyTz,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                recentDays: RecentDays,
                debugLastDays: DebugLastDays,
                runKind: ModelRunKind.Analytics);

            Print(snapshot);
        }

        public static void Print(BacktestDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            ConsoleStyler.WriteHeader("==== MODEL STATS ====");

            var multi = snapshot.ModelStats;
            var meta = multi.Meta;

            if (meta.TotalRecordsCount == 0)
            {
                Console.WriteLine("[model-stats] no records, nothing to print.");
                return;
            }

            var fullSeg = snapshot.Segments.FirstOrDefault(s => s.Kind == BacktestDiagnosticsSegmentKind.Full);
            string fullRange = fullSeg?.EntryFromUtc.HasValue == true
                ? $"{fullSeg.EntryFromUtc:yyyy-MM-dd}..{fullSeg.EntryToUtc:yyyy-MM-dd}"
                : "n/a";

            Console.WriteLine(
                $"[model-stats] full records period = {fullRange}, totalRecords = {meta.TotalRecordsCount}");

            Console.WriteLine(
                $"[model-stats] runKind={meta.RunKind}, " +
                $"trainUntil={meta.TrainUntilIsoDate}, " +
                $"train={meta.TrainRecordsCount}, " +
                $"oos={meta.OosRecordsCount}, " +
                $"total={meta.TotalRecordsCount}, " +
                $"recentDays={meta.RecentDays}, " +
                $"recentRecords={meta.RecentRecordsCount}");

            if (snapshot.Meta.ShuffleSanityAccuracyPct.HasValue && snapshot.Meta.ShuffleSanityN > 0)
            {
                Console.WriteLine(
                    $"[model-stats][shuffle] sanity acc on shuffled labels = {snapshot.Meta.ShuffleSanityAccuracyPct:0.0}% (n={snapshot.Meta.ShuffleSanityN})");
            }
            else
            {
                Console.WriteLine("[model-stats][shuffle] no records for sanity test – skipped.");
            }

            var componentFlags = ResolveComponentFlags();

            PrintSegmentIfExists(snapshot, ModelStatsSegmentKind.OosOnly, BacktestDiagnosticsSegmentKind.Oos, "OOS segment", componentFlags);
            PrintSegmentIfExists(snapshot, ModelStatsSegmentKind.TrainOnly, BacktestDiagnosticsSegmentKind.Train, "Train segment", componentFlags);
            PrintSegmentIfExists(snapshot, ModelStatsSegmentKind.RecentWindow, BacktestDiagnosticsSegmentKind.Recent, "Recent segment", componentFlags);
            PrintSegmentIfExists(snapshot, ModelStatsSegmentKind.FullHistory, BacktestDiagnosticsSegmentKind.Full, "Full-history segment", componentFlags);
        }

        private static void PrintSegmentIfExists(
            BacktestDiagnosticsSnapshot snapshot,
            ModelStatsSegmentKind kind,
            BacktestDiagnosticsSegmentKind diagKind,
            string segmentTitle,
            ModelComponentPrintFlags componentFlags)
        {
            var segment = snapshot.ModelStats.Segments
                .FirstOrDefault(s => s.Kind == kind);

            if (segment == null)
                return;

            ConsoleStyler.WriteHeader(
                $"{segmentTitle}: {segment.Label} " +
                $"[{segment.FromDateUtc:yyyy-MM-dd}..{segment.ToDateUtc:yyyy-MM-dd}, " +
                $"records={segment.RecordsCount}]");

            PrintDailyConfusion(segment.Stats.Daily, scopeLabel: segment.Label);
            Console.WriteLine();

            PrintTrendDirectionConfusion(segment.Stats.Trend, scopeLabel: segment.Label);
            Console.WriteLine();

            PrintSlStats(segment.Stats.Sl);
            Console.WriteLine();

            if (componentFlags != ModelComponentPrintFlags.None)
            {
                var diagSeg = snapshot.Segments.FirstOrDefault(s => s.Kind == diagKind);
                if (diagSeg != null)
                {
                    PrintComponentStats(diagSeg.ComponentStats, segment.Label, componentFlags);
                    Console.WriteLine();
                }
            }
        }

        private static void PrintDailyConfusion(DailyConfusionStats daily, string? scopeLabel = null)
        {
            var title = scopeLabel == null
                ? "Daily label confusion (3-class)"
                : $"Daily label confusion (3-class) [{scopeLabel}]";

            ConsoleStyler.WriteHeader(title);

            var t = new TextTable();
            t.AddHeader("true label", "pred 0", "pred 1", "pred 2", "correct", "total", "acc %");

            double baseline = 100.0 / 3.0;

            foreach (var row in daily.Rows)
            {
                var line = new[]
                {
                    row.LabelName,
                    row.Pred0.ToString(),
                    row.Pred1.ToString(),
                    row.Pred2.ToString(),
                    row.Correct.ToString(),
                    row.Total.ToString(),
                    $"{row.AccuracyPct:0.0}%"
                };

                var color = row.AccuracyPct >= baseline
                    ? ConsoleStyler.GoodColor
                    : ConsoleStyler.BadColor;

                t.AddColoredRow(color, line);
            }

            t.AddRow(
                "Accuracy (overall)",
                "",
                "",
                "",
                daily.OverallCorrect.ToString(),
                daily.OverallTotal.ToString(),
                $"{daily.OverallAccuracyPct:0.0}%"
            );

            t.WriteToConsole();
        }

        private static void PrintTrendDirectionConfusion(TrendDirectionStats trend, string? scopeLabel = null)
        {
            var title = scopeLabel == null
                ? "Trend-direction confusion (DOWN vs UP)"
                : $"Trend-direction confusion (DOWN vs UP) [{scopeLabel}]";

            ConsoleStyler.WriteHeader(title);

            var t = new TextTable();
            t.AddHeader("true trend", "pred DOWN", "pred UP", "correct", "total", "acc %");

            double baseline = 50.0;

            foreach (var row in trend.Rows)
            {
                var line = new[]
                {
                    row.Name,
                    row.PredDown.ToString(),
                    row.PredUp.ToString(),
                    row.Correct.ToString(),
                    row.Total.ToString(),
                    $"{row.AccuracyPct:0.0}%"
                };

                var color = row.AccuracyPct >= baseline
                    ? ConsoleStyler.GoodColor
                    : ConsoleStyler.BadColor;

                t.AddColoredRow(color, line);
            }

            var overallColor = trend.OverallAccuracyPct >= baseline
                ? ConsoleStyler.GoodColor
                : ConsoleStyler.BadColor;

            t.AddColoredRow(
                overallColor,
                "Accuracy (overall)",
                "",
                "",
                trend.OverallCorrect.ToString(),
                trend.OverallTotal.ToString(),
                $"{trend.OverallAccuracyPct:0.0}%"
            );

            t.WriteToConsole();
        }

        private static void PrintSlStats(OptionalValue<SlStats> sl)
        {
            if (!sl.HasValue)
            {
                ConsoleStyler.WriteHeader("SL-model confusion (runtime, path-based)");
                Console.WriteLine($"[sl-model] отсутствует: {sl.MissingReason}");
                Console.WriteLine();
                ConsoleStyler.WriteHeader("SL-model metrics (runtime)");
                Console.WriteLine($"[sl-model] отсутствует: {sl.MissingReason}");
                return;
            }

            var value = sl.Value;
            var confusion = value.Confusion;
            var metrics = value.Metrics;

            ConsoleStyler.WriteHeader("SL-model confusion (runtime, path-based)");

            var t = new TextTable();
            t.AddHeader("day type", "pred LOW", "pred HIGH");
            t.AddRow("TP-day", confusion.TpLow.ToString(), confusion.TpHigh.ToString());
            t.AddRow("SL-day", confusion.SlLow.ToString(), confusion.SlHigh.ToString());
            t.AddRow("SL saved (potential)", confusion.SlSaved.ToString(), "");
            t.WriteToConsole();
            Console.WriteLine();

            ConsoleStyler.WriteHeader("SL-model metrics (runtime)");
            var mTab = new TextTable();
            mTab.AddHeader("metric", "value");
            mTab.AddRow("coverage (scored / signal days)", $"{metrics.Coverage * 100.0:0.0}%  ({confusion.ScoredDays}/{confusion.TotalSignalDays})");
            mTab.AddRow("TPR / Recall (SL-day)", $"{metrics.Tpr * 100.0:0.0}%");
            mTab.AddRow("FPR (TP-day)", $"{metrics.Fpr * 100.0:0.0}%");
            mTab.AddRow("Precision (SL-day)", $"{metrics.Precision * 100.0:0.0}%");
            mTab.AddRow("F1 (SL-day)", $"{metrics.F1:0.000}");
            mTab.AddRow("PR-AUC (approx)", $"{metrics.PrAuc:0.000}");
            mTab.WriteToConsole();

            PrintSlSummaryLine(
                metrics.Coverage,
                metrics.Tpr,
                metrics.Fpr,
                metrics.Precision,
                metrics.F1,
                metrics.PrAuc);

            PrintSlThresholdSweep(value);
        }

        private static void PrintSlSummaryLine(
            double coverage,
            double tpr,
            double fpr,
            double precision,
            double f1,
            double prAuc)
        {
            double covPct = coverage * 100.0;
            double tprPct = tpr * 100.0;
            double fprPct = fpr * 100.0;
            double precPct = precision * 100.0;

            bool good =
                covPct >= 50.0 &&
                tprPct >= 60.0 &&
                fprPct <= 40.0 &&
                f1 >= 0.40;

            var color = good ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;

            string summary =
                $"SL-model summary: " +
                $"cov={covPct:0.0}%, " +
                $"TPR={tprPct:0.0}%, " +
                $"FPR={fprPct:0.0}%, " +
                $"Prec={precPct:0.0}%, " +
                $"F1={f1:0.000}, " +
                $"PR-AUC={prAuc:0.000}";

            WriteColoredLine(color, summary);
        }

        private static void PrintSlThresholdSweep(SlStats sl)
        {
            ConsoleStyler.WriteHeader("SL threshold sweep (runtime)");

            var thresholds = sl.Thresholds;
            var confusion = sl.Confusion;

            if (thresholds == null || thresholds.Count == 0)
            {
                Console.WriteLine("[sl-thr] no days with both TP/SL outcome and SlProb available – sweep skipped.");
                return;
            }

            Console.WriteLine($"[sl-thr] base set: totalDays={confusion.TotalOutcomeDays}, SL-days={confusion.TotalSlDays}, TP-days={confusion.TotalTpDays}");

            var t = new TextTable();
            t.AddHeader("thr", "TPR(SL)", "FPR(TP)", "pred HIGH %", "high / total");

            foreach (var row in thresholds)
            {
                var cells = new[]
                {
                    row.Threshold.ToString("0.00"),
                    $"{row.TprPct:0.0}%",
                    $"{row.FprPct:0.0}%",
                    $"{row.PredHighPct:0.0}%",
                    $"{row.HighTotal}/{row.TotalDays}"
                };

                var color = row.IsGood
                    ? ConsoleStyler.GoodColor
                    : ConsoleStyler.BadColor;

                t.AddColoredRow(color, cells);
            }

            t.WriteToConsole();
        }

        private static void PrintComponentStats(
            BacktestDiagnosticsComponentStatsSnapshot stats,
            string scopeLabel,
            ModelComponentPrintFlags flags)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));

            ConsoleStyler.WriteHeader($"MODEL COMPONENTS [{scopeLabel}]");

            if (flags.HasFlag(ModelComponentPrintFlags.LayerTriClass))
            {
                PrintTriClassSummary(stats);
                Console.WriteLine();
            }

            if (flags.HasFlag(ModelComponentPrintFlags.MoveModel))
            {
                PrintMoveModelSummary(stats.Move);
                Console.WriteLine();
            }

            if (flags.HasFlag(ModelComponentPrintFlags.DirModel))
            {
                PrintDirModelSummary(stats.Dir);
                Console.WriteLine();
            }

            if (flags.HasFlag(ModelComponentPrintFlags.MicroModel))
            {
                PrintMicroModelSummary(stats.Micro);
            }
        }

        private static void PrintTriClassSummary(BacktestDiagnosticsComponentStatsSnapshot stats)
        {
            var t = new TextTable();
            t.AddHeader("layer", "N", "correct", "accuracy");

            t.AddRow("Day", stats.Day.N.ToString(), stats.Day.Correct.ToString(), FormatPct(stats.Day.Accuracy));
            t.AddRow("Day+Micro", stats.DayMicro.N.ToString(), stats.DayMicro.Correct.ToString(), FormatPct(stats.DayMicro.Accuracy));
            t.AddRow("Total", stats.Total.N.ToString(), stats.Total.Correct.ToString(), FormatPct(stats.Total.Accuracy));

            t.WriteToConsole();
        }

        private static void PrintMoveModelSummary(MoveComponentStats stats)
        {
            var t = new TextTable();
            t.AddHeader("model", "N", "correct", "accuracy", "true_move", "true_flat", "pred_move", "pred_flat");
            t.AddRow(
                "Move (move vs flat)",
                stats.N.ToString(),
                stats.Correct.ToString(),
                FormatPct(stats.Accuracy),
                stats.TrueMove.ToString(),
                stats.TrueFlat.ToString(),
                stats.PredMove.ToString(),
                stats.PredFlat.ToString());
            t.WriteToConsole();
        }

        private static void PrintDirModelSummary(DirComponentStats stats)
        {
            var t = new TextTable();
            t.AddHeader("model", "N", "correct", "accuracy", "move_pred_true", "move_true_but_truth_flat");
            t.AddRow(
                "Dir (up vs down, move-days)",
                stats.N.ToString(),
                stats.Correct.ToString(),
                FormatPct(stats.Accuracy),
                stats.MovePredTrue.ToString(),
                stats.MoveTrueButTruthFlat.ToString());
            t.WriteToConsole();
        }

        private static void PrintMicroModelSummary(OptionalValue<MicroComponentStats> stats)
        {
            if (!stats.HasValue)
            {
                string reason = stats.MissingReason ?? "<no-reason>";
                Console.WriteLine($"[model-components] micro stats missing: reason={reason}");
                return;
            }

            var value = stats.Value;
            var t = new TextTable();
            t.AddHeader("model", "N", "correct", "accuracy", "fact_micro_days", "pred_micro_days", "coverage");
            t.AddRow(
                "Micro (flat days)",
                value.N.ToString(),
                value.Correct.ToString(),
                FormatPct(value.Accuracy),
                value.FactMicroDays.ToString(),
                value.PredMicroDays.ToString(),
                FormatPct(value.Coverage));
            t.WriteToConsole();
        }

        private static string FormatPct(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            return $"{value * 100.0:0.0}%";
        }

        private static ModelComponentPrintFlags ResolveComponentFlags()
        {
            string? raw = Environment.GetEnvironmentVariable("MODEL_STATS_COMPONENTS");
            if (string.IsNullOrWhiteSpace(raw))
                return ModelComponentPrintFlags.All;

            var parts = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.ToLowerInvariant())
                .ToArray();

            if (parts.Length == 0)
                return ModelComponentPrintFlags.All;

            ModelComponentPrintFlags flags = ModelComponentPrintFlags.None;

            foreach (var p in parts)
            {
                if (p == "all")
                {
                    flags = ModelComponentPrintFlags.All;
                    break;
                }

                if (p == "day" || p == "layers" || p == "tri" || p == "tri-class")
                    flags |= ModelComponentPrintFlags.LayerTriClass;
                else if (p == "move")
                    flags |= ModelComponentPrintFlags.MoveModel;
                else if (p == "dir" || p == "direction")
                    flags |= ModelComponentPrintFlags.DirModel;
                else if (p == "micro")
                    flags |= ModelComponentPrintFlags.MicroModel;
                else
                    throw new InvalidOperationException($"[model-components] unknown MODEL_STATS_COMPONENTS flag: '{p}'. raw='{raw}'.");
            }

            return flags == ModelComponentPrintFlags.None ? ModelComponentPrintFlags.All : flags;
        }

        private static void WriteColoredLine(ConsoleColor color, string text)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }
    }
}
