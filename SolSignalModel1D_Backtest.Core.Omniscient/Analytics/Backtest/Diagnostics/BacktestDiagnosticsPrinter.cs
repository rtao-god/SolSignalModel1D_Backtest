using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Diagnostics
{
    public static class BacktestDiagnosticsPrinter
    {
        public static void Print(BacktestDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            PrintCoverage(snapshot);
            PrintSegments(snapshot);

            AggregationProbsPrinter.Print(snapshot.AggregationProbs);
            AggregationMetricsPrinter.Print(snapshot.AggregationMetrics);
            MicroStatsPrinter.Print(snapshot.MicroStats);
            BacktestModelStatsPrinter.Print(snapshot);
        }

        private static void PrintCoverage(BacktestDiagnosticsSnapshot snapshot)
        {
            var cov = snapshot.Coverage;

            ConsoleStyler.WriteHeader("==== DATA COVERAGE / INTEGRITY ====");

            var t = new TextTable();
            t.AddHeader("metric", "value");
            t.AddRow("records_total", cov.RecordsTotal.ToString());
            t.AddRow("records_excluded_by_windowing", cov.RecordsExcludedByWindowing.ToString());
            t.AddRow("truth_daily_label_available", cov.TruthDailyLabelAvailable.ToString());
            t.AddRow("micro_truth_available", cov.MicroTruthAvailable.ToString());
            t.AddRow("micro_gating_days", cov.MicroGatingDays.ToString());
            t.AddRow("sl_score_available", cov.SlScoreAvailable.ToString());
            t.AddRow("sl_label_available", cov.SlLabelAvailable.ToString());
            t.AddRow("sl_eval_base", cov.SlEvalBase.ToString());
            t.WriteToConsole();

            if (cov.MissingReasons.Count > 0)
            {
                Console.WriteLine("[missing] reasons (top):");

                foreach (var kv in cov.MissingReasons
                    .OrderByDescending(p => p.Value)
                    .ThenBy(p => p.Key)
                    .Take(12))
                {
                    Console.WriteLine($"  {kv.Key} = {kv.Value}");
                }
            }
        }

        private static void PrintSegments(BacktestDiagnosticsSnapshot snapshot)
        {
            ConsoleStyler.WriteHeader("==== SEGMENTS BASES ====");

            foreach (var seg in snapshot.Segments)
            {
                string entry = seg.EntryFromUtc.HasValue
                    ? $"{seg.EntryFromUtc:yyyy-MM-dd}..{seg.EntryToUtc:yyyy-MM-dd}"
                    : "(empty)";

                string exit = seg.ExitFromUtc.HasValue
                    ? $"{seg.ExitFromUtc:yyyy-MM-dd}..{seg.ExitToUtc:yyyy-MM-dd}"
                    : "(n/a)";

                Console.WriteLine(
                    $"[{seg.Kind}] {seg.Label} entry={entry}, exit={exit}, records={seg.RecordsCount}");

                Console.WriteLine(
                    $"  base: N_daily_eval={seg.Bases.NDailyEval}, N_trend_eval={seg.Bases.NTrendEval}, " +
                    $"N_micro_eval={seg.Bases.NMicroEval}, N_sl_eval={seg.Bases.NSlEval}");

                Console.WriteLine(
                    $"  micro: truth={seg.Bases.NMicroTruth}, gating={seg.Bases.NMicroGating}");

                Console.WriteLine(
                    $"  sl: score={seg.Bases.NSlScore}, label={seg.Bases.NSlLabel}, eval={seg.Bases.NSlEval}");

                if (seg.Missing.Reasons.Count > 0)
                {
                    var top = seg.Missing.Reasons
                        .OrderByDescending(p => p.Value)
                        .ThenBy(p => p.Key)
                        .Take(6)
                        .ToList();

                    string tail = string.Join(", ", top.Select(p => $"{p.Key}={p.Value}"));
                    Console.WriteLine($"  missing: {tail}");
                }
            }
        }
    }
}
