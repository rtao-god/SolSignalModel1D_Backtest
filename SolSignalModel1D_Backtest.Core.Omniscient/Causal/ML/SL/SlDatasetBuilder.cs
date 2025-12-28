using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.ML.SL
{
    public sealed class SlDataset
    {
        public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; init; }

        public List<BacktestRecord> MorningRows { get; init; } = new List<BacktestRecord>();

        public List<SlHitSample> Samples { get; init; } = new List<SlHitSample>();
    }

    public static class SlDatasetBuilder
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        public static SlDataset Build(
            List<BacktestRecord> rows,
            IReadOnlyList<Candle1h>? sol1h,
            IReadOnlyList<Candle1m>? sol1m,
            Dictionary<DateTime, Candle6h> sol6hDict,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
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
                .Where(r => NyWindowing.IsNyMorning(new EntryUtc(r.Causal.EntryUtc.Value), NyTz))
                .ToList();

            var rowsTrain = new List<BacktestRecord>(morningOrdered.Count);

            for (int i = 0; i < morningOrdered.Count; i++)
            {
                var r = morningOrdered[i];

                var cls = NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: new EntryUtc(r.Causal.EntryUtc.Value),
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyTz,
                    baselineExitDayKeyUtc: out _);

                if (cls == NyTrainSplit.EntryClass.Train)
                    rowsTrain.Add(r);
            }

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

            var filteredSamples = new List<SlHitSample>(allSamples.Count);

            foreach (var sample in allSamples)
            {
                var entry = new EntryUtc(sample.EntryUtc);

                if (!NyWindowing.TryComputeBaselineExitUtc(entry, NyTz, out var exitUtc))
                    continue;

                var exitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value);

                if (exitDayKeyUtc.Value <= trainUntilExitDayKeyUtc.Value)
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
                .OrderBy(r => r.EntryDayKeyUtc.Value)
                .GroupBy(r => r.EntryDayKeyUtc.Value)
                .Select(g => g.First())
                .ToList();

            return new SlDataset
            {
                TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                MorningRows = distinctMorning,
                Samples = filteredSamples
            };
        }

    }
}

