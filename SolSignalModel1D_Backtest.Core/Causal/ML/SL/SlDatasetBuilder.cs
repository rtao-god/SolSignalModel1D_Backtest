using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.SL
{
    /// <summary>
    /// Контейнер датасета для SL-модели.
    /// Контракт: граница train/oos — exit-day-key (DayKeyUtc), а не DateTime.
    /// </summary>
    public sealed class SlDataset
    {
        public DayKeyUtc TrainUntilExitDayKeyUtc { get; init; }

        public List<BacktestRecord> MorningRows { get; init; } = new List<BacktestRecord>();

        public List<SlHitSample> Samples { get; init; } = new List<SlHitSample>();
    }

    /// <summary>
    /// Builder SL-датасета.
    /// </summary>
    public static class SlDatasetBuilder
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        public static SlDataset Build(
            List<BacktestRecord> rows,
            IReadOnlyList<Candle1h>? sol1h,
            IReadOnlyList<Candle1m>? sol1m,
            Dictionary<DateTime, Candle6h> sol6hDict,
            DayKeyUtc trainUntilExitDayKeyUtc,
            double tpPct,
            double slPct,
            Func<BacktestRecord, bool>? strongSelector)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (sol6hDict == null) throw new ArgumentNullException(nameof(sol6hDict));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            if (sol1m == null || sol1m.Count == 0)
                throw new InvalidOperationException("[SlDatasetBuilder] sol1m is required and must be non-empty.");

            if (sol1h == null || sol1h.Count == 0)
                throw new InvalidOperationException("[SlDatasetBuilder] sol1h is required and must be non-empty.");

            var morningOrdered = rows
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .Where(r => NyWindowing.IsNyMorning(r.Causal.EntryUtc, NyTz))
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: morningOrdered,
                entrySelector: r => r.Causal.EntryUtc,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: NyTz);

            var rowsTrain = split.Train;

            if (rowsTrain.Count == 0)
            {
                return new SlDataset
                {
                    TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                    MorningRows = new List<BacktestRecord>(),
                    Samples = new List<SlHitSample>()
                };
            }

            var allSamples = SlOfflineBuilder.Build(
                rows: rowsTrain,
                sol1h: sol1h,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                tpPct: tpPct,
                slPct: slPct,
                strongSelector: strongSelector);

            if (allSamples.Count == 0)
            {
                return new SlDataset
                {
                    TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                    MorningRows = new List<BacktestRecord>(),
                    Samples = new List<SlHitSample>()
                };
            }

            // Safety-cut по baseline-exit day-key (на случай неконсистентного EntryUtc в sample).
            var filteredSamples = new List<SlHitSample>(allSamples.Count);

            foreach (var sample in allSamples)
            {
                var entry = new EntryUtc(sample.EntryUtc);

                if (!NyWindowing.TryComputeBaselineExitUtc(entry, NyTz, out var exitUtc))
                    continue;

                var exitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(exitUtc.Value);

                if (exitDayKeyUtc <= trainUntilExitDayKeyUtc)
                    filteredSamples.Add(sample);
            }

            if (filteredSamples.Count == 0)
            {
                return new SlDataset
                {
                    TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                    MorningRows = new List<BacktestRecord>(),
                    Samples = new List<SlHitSample>()
                };
            }

            var morningByEntryUtc = rowsTrain
                .GroupBy(r => r.Causal.EntryUtc.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var morningRows = new List<BacktestRecord>(filteredSamples.Count);

            foreach (var sample in filteredSamples)
            {
                if (!morningByEntryUtc.TryGetValue(sample.EntryUtc, out var row))
                    throw new InvalidOperationException($"[SlDatasetBuilder] No BacktestRecord for sample entryUtc={sample.EntryUtc:O}.");

                morningRows.Add(row);
            }

            var distinctMorning = morningRows
                .OrderBy(r => r.Causal.DayKeyUtc.Value)
                .GroupBy(r => r.Causal.DayKeyUtc.Value)
                .Select(g => g.First())
                .ToList();

            return new SlDataset
            {
                TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                MorningRows = distinctMorning,
                Samples = filteredSamples
            };
        }

        /// <summary>
        /// Legacy overload: принимает DateTime (UTC момент) и переводит в exit-day-key.
        /// Оставлен для совместимости со старыми call-sites.
        /// </summary>
        [Obsolete("Use Build(..., DayKeyUtc trainUntilExitDayKeyUtc, ...)")]
        public static SlDataset Build(
            List<BacktestRecord> rows,
            IReadOnlyList<Candle1h>? sol1h,
            IReadOnlyList<Candle1m>? sol1m,
            Dictionary<DateTime, Candle6h> sol6hDict,
            DateTime trainUntilUtc,
            double tpPct,
            double slPct,
            Func<BacktestRecord, bool>? strongSelector)
        {
            if (trainUntilUtc == default)
                throw new ArgumentException("trainUntilUtc must be initialized (non-default).", nameof(trainUntilUtc));
            if (trainUntilUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"trainUntilUtc must be UTC. Got Kind={trainUntilUtc.Kind}, t={trainUntilUtc:O}.", nameof(trainUntilUtc));

            var trainUntilExitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc);

            return Build(
                rows: rows,
                sol1h: sol1h,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                tpPct: tpPct,
                slPct: slPct,
                strongSelector: strongSelector);
        }
    }
}
