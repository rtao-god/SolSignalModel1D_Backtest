using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Causal.Analytics.Backtest.Adapters;
using OmniscientModelStatsPrinter =
    SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers.BacktestModelStatsPrinter;
using System.Globalization;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
{
    public sealed class BacktestRunner
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        public void Run(
            IReadOnlyList<LabeledCausalRow> mornings,
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> candles1m,
            IReadOnlyList<RollingLoop.PolicySpec> policies,
            BacktestConfig config,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            if (mornings == null) throw new ArgumentNullException(nameof(mornings));
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (candles1m == null) throw new ArgumentNullException(nameof(candles1m));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            // ===== records coverage + split (по baseline-exit) =====
            int recordsCount = records.Count;

            DateTime? recMin = null;
            DateTime? recMax = null;

            var recordDays = new HashSet<EntryDayKeyUtc>(recordsCount);

            // excludedDays — по entry-day-key ("дни записей", исключённые апстримом: weekend-entry / no baseline-exit).
            var excludedDays = new HashSet<EntryDayKeyUtc>(Math.Min(recordsCount, 256));

            int trainCount = 0, oosCount = 0, exclCount = 0;

            for (int i = 0; i < recordsCount; i++)
            {
                var r = records[i];

                var entryDayKey = r.EntryDayKeyUtc;
                var dayDt = entryDayKey.Value;

                if (!recMin.HasValue || dayDt < recMin.Value) recMin = dayDt;
                if (!recMax.HasValue || dayDt > recMax.Value) recMax = dayDt;

                recordDays.Add(entryDayKey);

                var cls = NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: r.EntryUtc,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyTz,
                    baselineExitDayKeyUtc: out _);

                if (cls == NyTrainSplit.EntryClass.Excluded)
                {
                    excludedDays.Add(entryDayKey);
                    exclCount++;
                }
                else if (cls == NyTrainSplit.EntryClass.Train)
                {
                    trainCount++;
                }
                else
                {
                    oosCount++;
                }
            }

            if (recordsCount > 0)
                Console.WriteLine($"[diag-path] records: count={recordsCount}, range={recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}");
            else
                Console.WriteLine("[diag-path] records: count=0");

            Console.WriteLine(
                $"[diag-path] boundary trainUntilExitDayKey={trainUntilExitDayKeyUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, " +
                $"train={trainCount}, oos={oosCount}, excluded={exclCount}");

            // ===== mornings coverage =====
            int morningsCount = mornings.Count;
            DateTime? mornMin = null;
            DateTime? mornMax = null;

            var morningDays = new HashSet<EntryDayKeyUtc>(morningsCount);

            for (int i = 0; i < morningsCount; i++)
            {
                var day = mornings[i].EntryDayKeyUtc;
                var dayDt = day.Value;

                if (!mornMin.HasValue || dayDt < mornMin.Value) mornMin = dayDt;
                if (!mornMax.HasValue || dayDt > mornMax.Value) mornMax = dayDt;

                morningDays.Add(day);
            }

            if (morningsCount > 0)
                Console.WriteLine($"[diag-path] mornings: count={morningsCount}, range={mornMin:yyyy-MM-dd}..{mornMax:yyyy-MM-dd}");
            else
                Console.WriteLine("[diag-path] mornings: count=0");

            // ===== mismatch sample =====
            var recordOnly = new List<EntryDayKeyUtc>();
            foreach (var d in recordDays)
            {
                if (!morningDays.Contains(d))
                    recordOnly.Add(d);
            }
            recordOnly.Sort((a, b) => a.Value.CompareTo(b.Value));

            var morningOnly = new List<EntryDayKeyUtc>();
            foreach (var d in morningDays)
            {
                if (!recordDays.Contains(d))
                    morningOnly.Add(d);
            }
            morningOnly.Sort((a, b) => a.Value.CompareTo(b.Value));

            if (recordOnly.Count > 10) recordOnly = recordOnly.GetRange(0, 10);
            if (morningOnly.Count > 10) morningOnly = morningOnly.GetRange(0, 10);

            Console.WriteLine("[diag-path] dates in records but not in mornings (first 10):");
            if (recordOnly.Count == 0) Console.WriteLine("  (none)");
            else
            {
                for (int i = 0; i < recordOnly.Count; i++)
                    Console.WriteLine($"  {recordOnly[i].Value:yyyy-MM-dd}");
            }

            Console.WriteLine("[diag-path] dates in mornings but not in records (first 10):");
            if (morningOnly.Count == 0) Console.WriteLine("  (none)");
            else
            {
                for (int i = 0; i < morningOnly.Count; i++)
                    Console.WriteLine($"  {morningOnly[i].Value:yyyy-MM-dd}");
            }

            // ===== Каузальная аналитика (split строго по baseline-exit) =====
            var eligible = new List<BacktestAggRow>(recordsCount);
            var excluded = new List<BacktestAggRow>(Math.Min(256, recordsCount));
            var train = new List<BacktestAggRow>(recordsCount);
            var oos = new List<BacktestAggRow>(Math.Min(256, recordsCount));

            for (int i = 0; i < recordsCount; i++)
            {
                var r = records[i];

                var agg = r.ToAggRow();

                var cls = NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: r.EntryUtc,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyTz,
                    baselineExitDayKeyUtc: out _);

                if (cls == NyTrainSplit.EntryClass.Excluded)
                {
                    excluded.Add(agg);
                    continue;
                }

                eligible.Add(agg);

                if (cls == NyTrainSplit.EntryClass.Train) train.Add(agg);
                else oos.Add(agg);
            }

            var sets = new AggregationInputSets
            {
                Boundary = new TrainBoundaryMeta(trainUntilExitDayKeyUtc),
                Eligible = eligible,
                Excluded = excluded,
                Train = train,
                Oos = oos
            };

            var probsSnap = AggregationProbsSnapshotBuilder.Build(sets, recentDays: 240, debugLastDays: 10);
            AggregationProbsPrinter.Print(probsSnap);

            var metricsSnap = AggregationMetricsSnapshotBuilder.Build(sets, recentDays: 240);
            AggregationMetricsPrinter.Print(metricsSnap);

            var microSnap = MicroStatsSnapshotBuilder.Build(sets.Eligible);
            MicroStatsPrinter.Print(microSnap);

            OmniscientModelStatsPrinter.Print(
                records: records,
                sol1m: candles1m,
                dailyTpPct: config.DailyTpPct,
                dailySlPct: config.DailyStopPct,
                nyTz: NyTz,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc
            );
        }
    }
}
