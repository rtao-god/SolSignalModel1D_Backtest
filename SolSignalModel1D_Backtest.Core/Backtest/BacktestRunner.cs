using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Adapters;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolSignalModel1D_Backtest.Core.Backtest
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
            DayKeyUtc trainUntilDayKeyUtc)
        {
            if (mornings == null) throw new ArgumentNullException(nameof(mornings));
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (candles1m == null) throw new ArgumentNullException(nameof(candles1m));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (trainUntilDayKeyUtc.Equals(default(DayKeyUtc)))
                throw new ArgumentException("trainUntilDayKeyUtc must be initialized (non-default).", nameof(trainUntilDayKeyUtc));

            // ===== records coverage + split =====
            int recordsCount = records.Count;

            DateTime? recMin = null;
            DateTime? recMax = null;

            var recordDays = new HashSet<DayKeyUtc>(recordsCount);

            var excludedDays = new HashSet<DayKeyUtc>(Math.Min(recordsCount, 256));

            for (int i = 0; i < recordsCount; i++)
            {
                var r = records[i];

                var day = CausalTimeKey.DayKeyUtc(r);
                var dayDt = day.Value;

                if (!recMin.HasValue || dayDt < recMin.Value) recMin = dayDt;
                if (!recMax.HasValue || dayDt > recMax.Value) recMax = dayDt;

                recordDays.Add(day);

                var entry = CausalTimeKey.EntryUtc(r);
                if (IsNyWeekendEntry(entry))
                    excludedDays.Add(day);
            }

            if (recordsCount > 0)
                Console.WriteLine($"[diag-path] records: count={recordsCount}, range={recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}");
            else
                Console.WriteLine("[diag-path] records: count=0");

            int trainCount = 0, oosCount = 0, exclCount = 0;

            for (int i = 0; i < recordsCount; i++)
            {
                var r = records[i];
                var day = CausalTimeKey.DayKeyUtc(r);

                if (excludedDays.Contains(day))
                {
                    exclCount++;
                    continue;
                }

                if (day.Value <= trainUntilDayKeyUtc.Value) trainCount++;
                else oosCount++;
            }

            Console.WriteLine(
                $"[diag-path] boundary trainUntil={trainUntilDayKeyUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, " +
                $"train={trainCount}, oos={oosCount}, excluded={exclCount}");

            // ===== mornings coverage =====
            int morningsCount = mornings.Count;
            DateTime? mornMin = null;
            DateTime? mornMax = null;

            var morningDays = new HashSet<DayKeyUtc>(morningsCount);

            for (int i = 0; i < morningsCount; i++)
            {
                var day = CausalTimeKey.DayKeyUtc(mornings[i]);
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
            var recordOnly = new List<DayKeyUtc>();
            foreach (var d in recordDays)
            {
                if (!morningDays.Contains(d))
                    recordOnly.Add(d);
            }
            recordOnly.Sort((a, b) => a.Value.CompareTo(b.Value));

            var morningOnly = new List<DayKeyUtc>();
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

            // ===== Каузальная аналитика =====
            var aggAll = new List<BacktestAggRow>(records.Count);
            for (int i = 0; i < records.Count; i++)
                aggAll.Add(records[i].ToAggRow());

            var eligible = new List<BacktestAggRow>(aggAll.Count);
            var excluded = new List<BacktestAggRow>(Math.Min(256, aggAll.Count));

            for (int i = 0; i < aggAll.Count; i++)
            {
                var r = aggAll[i];
                if (excludedDays.Contains(r.DayUtc)) excluded.Add(r);
                else eligible.Add(r);
            }

            var train = new List<BacktestAggRow>(eligible.Count);
            var oos = new List<BacktestAggRow>(Math.Min(256, eligible.Count));

            for (int i = 0; i < eligible.Count; i++)
            {
                var r = eligible[i];
                if (r.DayUtc.Value <= trainUntilDayKeyUtc.Value) train.Add(r);
                else oos.Add(r);
            }

            var sets = new AggregationInputSets
            {
                Boundary = new TrainBoundaryMeta(trainUntilDayKeyUtc),
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
        }

        private static bool IsNyWeekendEntry(EntryUtc entry)
        {
            if (entry.IsDefault)
                throw new ArgumentException("[diag-path] entry must be initialized (non-default).", nameof(entry));

            // Валидация UTC инварианта.
            var entryUtc = entry.Value;
            if (entryUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[diag-path] entryUtc must be UTC, got Kind={entryUtc.Kind}, t={entryUtc:O}.");

            return NyWindowing.IsWeekendInNy(entry, NyTz);
        }
    }
}
