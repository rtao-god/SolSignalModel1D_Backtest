using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Sl
{
    /// <summary>
    /// SlDatasetBuilder:
    /// - использует только дни, у которых entry <= trainUntil и baseline-exit <= trainUntil;
    /// - не зависит от хвоста (DayKeyUtc > trainUntil).
    /// </summary>
    public sealed class LeakageSlDatasetTests
    {
        [Fact]
        public void SlDataset_UsesOnlyRows_UntilTrainUntil_AndIsFutureBlind()
        {
            var allRows = BuildSyntheticRows(30, out var sol6hDict, out var sol1m);

            var maxDayKeyUtc = allRows.Last().Causal.DayKeyUtc.Value;
            var trainUntil = maxDayKeyUtc.AddDays(-10);

            var rowsA = CloneRows(allRows);
            var rowsB = MutateFutureTail(CloneRows(allRows), trainUntil);

            var dsA = SlDatasetBuilder.Build(
                rows: rowsA,
                sol1h: null,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                trainUntil: trainUntil,
                tpPct: 0.03,
                slPct: 0.05,
                strongSelector: null);

            var dsB = SlDatasetBuilder.Build(
                rows: rowsB,
                sol1h: null,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                trainUntil: trainUntil,
                tpPct: 0.03,
                slPct: 0.05,
                strongSelector: null);

            Assert.All(dsA.MorningRows, r => Assert.True(r.Causal.DayKeyUtc.Value <= trainUntil));
            Assert.All(dsB.MorningRows, r => Assert.True(r.Causal.DayKeyUtc.Value <= trainUntil));

            Assert.Equal(dsA.Samples.Count, dsB.Samples.Count);

            for (int i = 0; i < dsA.Samples.Count; i++)
            {
                var a = dsA.Samples[i];
                var b = dsB.Samples[i];

                Assert.Equal(a.EntryUtc, b.EntryUtc);
                Assert.Equal(a.Label, b.Label);

                Assert.NotNull(a.Features);
                Assert.NotNull(b.Features);

                Assert.Equal(MlSchema.FeatureCount, a.Features.Length);
                Assert.Equal(MlSchema.FeatureCount, b.Features.Length);

                for (int j = 0; j < a.Features.Length; j++)
                    Assert.Equal(a.Features[j], b.Features[j]);
            }
        }

        private static List<BacktestRecord> BuildSyntheticRows(
            int count,
            out Dictionary<DateTime, Candle6h> sol6hDict,
            out List<Candle1m> sol1m)
        {
            var rows = new List<BacktestRecord>(count);
            var dict6h = new Dictionary<DateTime, Candle6h>(count);
            var all1m = new List<Candle1m>(count * 30);

            var start = new DateTime(2022, 4, 1, 8, 0, 0, DateTimeKind.Utc);

            for (int i = 0; rows.Count < count; i++)
            {
                var t = start.AddDays(i);
                if (!NyWindowing.TryCreateNyTradingEntryUtc(new EntryUtc(t), NyWindowing.NyTz, out var entryUtc))
                    continue;

                var openUtc = entryUtc.Value;
                double price = 100 + rows.Count;

                rows.Add(CreateBacktestRecord(
                    entryUtc: entryUtc,
                    isMorning: true,
                    minMove: 0.03));

                dict6h[openUtc] = new Candle6h
                {
                    OpenTimeUtc = openUtc,
                    Close = price,
                    High = price * 1.01,
                    Low = price * 0.99
                };

                for (int k = 0; k < 30; k++)
                {
                    all1m.Add(new Candle1m
                    {
                        OpenTimeUtc = openUtc.AddMinutes(k),
                        Open = price,
                        Close = price,
                        High = price * 1.05,
                        Low = price * 0.95
                    });
                }
            }

            sol6hDict = dict6h;
            sol1m = all1m;

            return rows.OrderBy(r => r.Causal.DayKeyUtc.Value).ToList();
        }

        private static BacktestRecord CreateBacktestRecord(EntryUtc entryUtc, bool isMorning, double minMove)
        {
            var vec = BuildVector64Deterministic(entryUtc.Value);

            var dayKey = new DayKeyUtc(new DateTime(
                entryUtc.Value.Year,
                entryUtc.Value.Month,
                entryUtc.Value.Day,
                0, 0, 0,
                DateTimeKind.Utc));

            var causal = new CausalPredictionRecord
            {
                EntryUtc = entryUtc,
                DayKeyUtc = dayKey,
                FeaturesVector = vec,
                Features = new CausalFeatures { IsMorning = isMorning },
                PredLabel = 1,
                PredLabel_Day = 1,
                PredLabel_DayMicro = 1,
                PredLabel_Total = 1,
                ProbUp_Day = 0.0,
                ProbFlat_Day = 1.0,
                ProbDown_Day = 0.0,
                ProbUp_DayMicro = 0.0,
                ProbFlat_DayMicro = 1.0,
                ProbDown_DayMicro = 0.0,
                ProbUp_Total = 0.0,
                ProbFlat_Total = 1.0,
                ProbDown_Total = 0.0,
                Conf_Day = 1.0,
                Conf_Micro = 0.0,
                MicroPredicted = false,
                PredMicroUp = false,
                PredMicroDown = false,
                RegimeDown = false,
                Reason = string.Empty,
                MinMove = minMove
            };

            var forward = new ForwardOutcomes
            {
                DayKeyUtc = dayKey,
                WindowEndUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz).Value,
                Entry = 100.0,
                MaxHigh24 = 105.0,
                MinLow24 = 95.0,
                Close24 = 100.0,
                DayMinutes = Array.Empty<Candle1m>(),
                MinMove = minMove,
                TrueLabel = 1,
                FactMicroUp = false,
                FactMicroDown = false
            };

            return new BacktestRecord
            {
                Causal = causal,
                Forward = forward
            };
        }

        private static double[] BuildVector64Deterministic(DateTime dateUtc)
        {
            var v = new double[MlSchema.FeatureCount];

            int seed = dateUtc.Year * 10_000 + dateUtc.Month * 100 + dateUtc.Day;
            var rng = new Random(seed);

            for (int i = 0; i < v.Length; i++)
                v[i] = (rng.NextDouble() - 0.5) * 2.0;

            return v;
        }

        private static List<BacktestRecord> CloneRows(List<BacktestRecord> src)
        {
            var res = new List<BacktestRecord>(src.Count);
            foreach (var r in src)
            {
                res.Add(CreateBacktestRecord(
                    entryUtc: r.Causal.EntryUtc,
                    isMorning: r.Causal.IsMorning == true,
                    minMove: r.Causal.MinMove));
            }
            return res;
        }

        private static List<BacktestRecord> MutateFutureTail(List<BacktestRecord> rows, DateTime trainUntilUtc)
        {
            var res = new List<BacktestRecord>(rows.Count);

            foreach (var r in rows)
            {
                if (r.Causal.DayKeyUtc.Value <= trainUntilUtc)
                {
                    res.Add(r);
                    continue;
                }

                res.Add(CreateBacktestRecord(
                    entryUtc: r.Causal.EntryUtc,
                    isMorning: !(r.Causal.IsMorning == true),
                    minMove: r.Causal.MinMove * 2.0));
            }

            return res;
        }
    }
}
