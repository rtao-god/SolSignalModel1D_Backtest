using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Snapshots.ModelStats
{
    public static class BacktestModelStatsMultiSnapshotBuilder
    {
        public static BacktestModelStatsMultiSnapshot Build(
            IReadOnlyList<BacktestRecord> allRecords,
            IReadOnlyList<Candle1m> sol1m,
            TimeZoneInfo nyTz,
            double dailyTpPct,
            double dailySlPct,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            int recentDays,
            ModelRunKind runKind)
        {
            if (allRecords == null) throw new ArgumentNullException(nameof(allRecords));
            if (sol1m == null) throw new ArgumentNullException(nameof(sol1m));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (trainUntilExitDayKeyUtc.IsDefault) throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));
            if (recentDays <= 0) throw new ArgumentOutOfRangeException(nameof(recentDays), "recentDays must be > 0.");

            var multi = new BacktestModelStatsMultiSnapshot
            {
                Meta =
                {
                    RunKind = runKind,
                    TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                    TrainUntilIsoDate = trainUntilExitDayKeyUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    RecentDays = recentDays
                }
            };

            if (allRecords.Count == 0)
            {
                multi.Meta.HasOos = false;
                multi.Meta.TrainRecordsCount = 0;
                multi.Meta.OosRecordsCount = 0;
                multi.Meta.TotalRecordsCount = 0;
                multi.Meta.RecentRecordsCount = 0;
                return multi;
            }

            static DateTime EntryUtcDt(BacktestRecord r) => r.Causal.EntryUtc.Value;

            var ordered = allRecords
                .OrderBy(EntryUtcDt)
                .ToList();

            var (trainRecords, oosRecords, excluded) = SplitByBaselineExitDayKey(
                ordered: ordered,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz);

            if (excluded.Count > 0)
            {
                throw new InvalidOperationException(
                    $"[model-stats] Found excluded records (baseline-exit undefined). " +
                    $"ExcludedCount={excluded.Count}. " +
                    $"This is a pipeline bug: filter out excluded days before analytics.");
            }

            var fullRecords = new List<BacktestRecord>(trainRecords.Count + oosRecords.Count);
            fullRecords.AddRange(trainRecords);
            fullRecords.AddRange(oosRecords);

            var maxEntryUtc = EntryUtcDt(fullRecords[^1]);

            var fromRecentUtc = maxEntryUtc.AddDays(-recentDays);
            var recentRecords = fullRecords
                .Where(r => EntryUtcDt(r) >= fromRecentUtc)
                .ToList();

            if (recentRecords.Count == 0)
                recentRecords = fullRecords;

            var meta = multi.Meta;
            meta.HasOos = oosRecords.Count > 0;
            meta.TrainRecordsCount = trainRecords.Count;
            meta.OosRecordsCount = oosRecords.Count;
            meta.TotalRecordsCount = fullRecords.Count;
            meta.RecentRecordsCount = recentRecords.Count;

            AddSegmentIfNotEmpty(
                multi,
                ModelStatsSegmentKind.OosOnly,
                label: "OOS-only (baseline-exit > trainUntil-exit-day-key)",
                oosRecords,
                sol1m,
                nyTz,
                dailyTpPct,
                dailySlPct);

            AddSegmentIfNotEmpty(
                multi,
                ModelStatsSegmentKind.TrainOnly,
                label: "Train-only (baseline-exit <= trainUntil-exit-day-key)",
                trainRecords,
                sol1m,
                nyTz,
                dailyTpPct,
                dailySlPct);

            AddSegmentIfNotEmpty(
                multi,
                ModelStatsSegmentKind.RecentWindow,
                label: $"Recent window (last {recentDays} days)",
                recentRecords,
                sol1m,
                nyTz,
                dailyTpPct,
                dailySlPct);

            AddSegmentIfNotEmpty(
                multi,
                ModelStatsSegmentKind.FullHistory,
                label: "Full history (eligible days)",
                fullRecords,
                sol1m,
                nyTz,
                dailyTpPct,
                dailySlPct);

            return multi;
        }

        private static void AddSegmentIfNotEmpty(
            BacktestModelStatsMultiSnapshot multi,
            ModelStatsSegmentKind kind,
            string label,
            IReadOnlyList<BacktestRecord> segmentRecords,
            IReadOnlyList<Candle1m> sol1m,
            TimeZoneInfo nyTz,
            double dailyTpPct,
            double dailySlPct)
        {
            if (segmentRecords == null) throw new ArgumentNullException(nameof(segmentRecords));
            if (segmentRecords.Count == 0)
                return;

            var stats = BacktestModelStatsSnapshotBuilder.Compute(
                records: segmentRecords,
                sol1m: sol1m,
                dailyTpPct: dailyTpPct,
                dailySlPct: dailySlPct,
                nyTz: nyTz);

            var segment = new BacktestModelStatsSegmentSnapshot
            {
                Kind = kind,
                Label = label,
                FromDateUtc = stats.FromDateUtc,
                ToDateUtc = stats.ToDateUtc,
                RecordsCount = segmentRecords.Count,
                Stats = stats
            };

            multi.Segments.Add(segment);
        }

        private static (List<BacktestRecord> Train, List<BacktestRecord> Oos, List<BacktestRecord> Excluded) SplitByBaselineExitDayKey(
            IReadOnlyList<BacktestRecord> ordered,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));
            if (trainUntilExitDayKeyUtc.IsDefault) throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized.", nameof(trainUntilExitDayKeyUtc));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var train = new List<BacktestRecord>(ordered.Count);
            var oos = new List<BacktestRecord>(Math.Min(ordered.Count, 512));
            var excluded = new List<BacktestRecord>(0);

            for (int i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                var entryDt = r.Causal.EntryUtc.Value;

                if (!NyWindowing.TryComputeBaselineExitUtc(new EntryUtc(entryDt), nyTz, out var exitUtc))
                {
                    excluded.Add(r);
                    continue;
                }

                var exitDayKey = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value);

                if (exitDayKey.Value <= trainUntilExitDayKeyUtc.Value) train.Add(r);
                else oos.Add(r);
            }

            return (train, oos, excluded);
        }
    }
}

