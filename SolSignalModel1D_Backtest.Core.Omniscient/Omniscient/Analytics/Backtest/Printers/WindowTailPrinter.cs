using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
{
    public static class WindowTailPrinter
    {
        private static EntryDayKeyUtc EntryDayKeyUtc(BacktestRecord r) => r.EntryDayKeyUtc;

        public static void PrintBlockTails(
            IReadOnlyList<LabeledCausalRow> mornings,
            IReadOnlyList<BacktestRecord> records,
            IEnumerable<BacktestPolicyResult> policyResults,
            int takeDays = 20,
            int skipDays = 30,
            string title = "Last day of each window (take → skip)")
        {
            if (mornings == null) throw new ArgumentNullException(nameof(mornings));
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (policyResults == null) throw new ArgumentNullException(nameof(policyResults));

            var recs = records.OrderBy(r => CausalTimeKey.EntryUtc(r).Value).ToList();
            var pol = policyResults.ToList();

            if (recs.Count == 0 || pol.Count == 0) return;
            if (takeDays <= 0) throw new ArgumentOutOfRangeException(nameof(takeDays), "takeDays must be > 0.");
            if (mornings.Count == 0) return;

            var byDate = recs.ToDictionary(r => EntryDayKeyUtc(r), r => r);

            ConsoleStyler.WriteHeader($"=== {title}: {takeDays} → {skipDays} ===");

            int i = 0;
            int blockIdx = 0;

            while (i < recs.Count)
            {
                int start = i;
                int endTake = Math.Min(i + takeDays, recs.Count);
                if (start >= endTake) break;

                var block = recs.GetRange(start, endTake - start);
                var lastRec = block[^1];

                blockIdx++;

                var blockStartDate = EntryDayKeyUtc(block.First());
                var blockEndDate = EntryDayKeyUtc(lastRec);

                var key = EntryDayKeyUtc(lastRec);
                if (!byDate.TryGetValue(key, out var dayRec))
                {
                    throw new InvalidOperationException(
                        $"[window-tail] Internal mismatch: byDate has no key={key.Value:O}. " +
                        "Это означает, что ключи словаря повреждены или EntryDayKeyUtc(...) нестабилен.");
                }

                ConsoleStyler.WriteHeader(
                    $"--- Блок {blockIdx} [{blockStartDate.Value:yyyy-MM-dd} .. {blockEndDate.Value:yyyy-MM-dd}] — последний день @ {blockEndDate.Value:yyyy-MM-dd} ---");

                PrintDayHead(dayRec, lastRec);
                PrintPolicyTradesForDay(blockEndDate, pol);

                i = endTake + skipDays;
            }

            Console.WriteLine();
        }

        private static void PrintDayHead(BacktestRecord? row, BacktestRecord r)
        {
            var t = new TextTable();
            t.AddHeader("field", "value");
            t.AddRow("pred", ClassToStr(r.PredLabel));
            t.AddRow("micro", r.PredMicroUp ? "UP" : r.PredMicroDown ? "DOWN" : "—");
            string microFact = r.MicroTruth.HasValue
                ? r.MicroTruth.Value == MicroTruthDirection.Up ? " (micro↑)" : " (micro↓)"
                : string.Empty;
            t.AddRow("fact", ClassToStr(r.TrueLabel) + microFact);
            t.AddRow("reason", r.Reason);
            t.AddRow("entry", r.Entry.ToString("0.0000"));
            t.AddRow("maxH / minL", $"{r.MaxHigh24:0.0000} / {r.MinLow24:0.0000}");
            t.AddRow("close24", r.Close24.ToString("0.0000"));
            t.AddRow("minMove", (r.MinMove * 100.0).ToString("0.00") + "%");

            if (row != null)
            {
                t.AddRow("path dir", PathDirToStr(row.Forward.PathFirstPassDir));
                t.AddRow(
                    "path firstPass",
                    row.Forward.PathFirstPassTimeUtc.HasValue
                        ? row.Forward.PathFirstPassTimeUtc.Value.ToString("yyyy-MM-dd HH:mm")
                        : "—"
                );
                t.AddRow(
                    "path up% / down%",
                    $"{row.Forward.PathReachedUpPct * 100.0:0.00}% / {row.Forward.PathReachedDownPct * 100.0:0.00}%"
                );
            }

            t.WriteToConsole();
            Console.WriteLine();
        }

        private static void PrintPolicyTradesForDay(EntryDayKeyUtc dayKeyUtc, IEnumerable<BacktestPolicyResult> policyResults)
        {
            ConsoleStyler.WriteHeader("Per-policy trades (this day)");
            var t = new TextTable();
            t.AddHeader("policy", "source/bucket", "side", "lev", "net %", "entry→exit", "liq?");

            foreach (var pr in policyResults.OrderBy(x => x.PolicyName))
            {
                var dayTrades = pr.Trades?
                    .Where(tr => tr.DateUtc.ToCausalDateUtc() == dayKeyUtc.Value.ToCausalDateUtc())
                    .OrderBy(tr => tr.EntryTimeUtc)
                    .ToList() ?? new List<PnLTrade>();

                if (dayTrades.Count == 0)
                {
                    t.AddRow(pr.PolicyName, "—", "—", "—", "—", "—", "—");
                    continue;
                }

                foreach (var tr in dayTrades)
                {
                    t.AddRow(
                        pr.PolicyName,
                        $"{tr.Source}/{tr.Bucket}",
                        tr.IsLong ? "LONG" : "SHORT",
                        $"{tr.LeverageUsed:0.##}x",
                        $"{tr.NetReturnPct:+0.00;-0.00}%",
                        $"{tr.EntryPrice:0.0000}→{tr.ExitPrice:0.0000}",
                        tr.IsLiquidated ? "YES" : "no"
                    );
                }
            }

            t.WriteToConsole();
            Console.WriteLine();
        }

        private static string ClassToStr(int c) => c switch
        {
            0 => "0 (down)",
            1 => "1 (flat)",
            2 => "2 (up)",
            _ => c.ToString()
        };

        private static string PathDirToStr(int dir) => dir switch
        {
            > 0 => "UP (first)",
            < 0 => "DOWN (first)",
            _ => "FLAT / none"
        };
    }
}
