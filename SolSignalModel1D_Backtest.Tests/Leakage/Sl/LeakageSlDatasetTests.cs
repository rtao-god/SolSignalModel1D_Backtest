using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using Xunit;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;

namespace SolSignalModel1D_Backtest.Tests.Leakage
{
    /// <summary>
    /// SlDatasetBuilder:
    /// - использует только дни, у которых entry <= trainUntil и baseline-exit <= trainUntil;
    /// - не зависит от хвоста (EntryDayKeyUtc > trainUntil).
    /// </summary>
    public sealed class LeakageSlDatasetTests
    {
        [Fact]
        public void SlDataset_UsesOnlyRows_UntilTrainUntil_AndIsFutureBlind()
        {
            var allRows = BuildSyntheticRows(30, out var sol6hDict, out var sol1h, out var sol1m);

            var trainUntilEntryUtc = allRows[^10].Causal.EntryUtc.Value;
            var trainUntil = TrainUntilExitDayKeyUtc.FromExitDayKeyUtc(
                NyWindowing.ComputeExitDayKeyUtc(
                    new EntryUtc(trainUntilEntryUtc),
                    NyWindowing.NyTz));

            var rowsA = CloneRows(allRows);
            var rowsB = MutateFutureTail(CloneRows(allRows), trainUntil);

            var dsA = SlDatasetBuilder.Build(
                rows: rowsA,
                sol1h: sol1h,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                trainUntilExitDayKeyUtc: trainUntil,
                tpPct: 0.03,
                slPct: 0.05,
                strongSelector: null);

            var dsB = SlDatasetBuilder.Build(
                rows: rowsB,
                sol1h: sol1h,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                trainUntilExitDayKeyUtc: trainUntil,
                tpPct: 0.03,
                slPct: 0.05,
                strongSelector: null);

            Assert.All(
                dsA.MorningRows,
                r => Assert.Equal(
                    NyTrainSplit.EntryClass.Train,
                    NyTrainSplit.ClassifyByBaselineExit(
                        entryUtc: r.Causal.EntryUtc,
                        trainUntilExitDayKeyUtc: trainUntil,
                        nyTz: NyWindowing.NyTz,
                        baselineExitDayKeyUtc: out _)));

            Assert.All(
                dsB.MorningRows,
                r => Assert.Equal(
                    NyTrainSplit.EntryClass.Train,
                    NyTrainSplit.ClassifyByBaselineExit(
                        entryUtc: r.Causal.EntryUtc,
                        trainUntilExitDayKeyUtc: trainUntil,
                        nyTz: NyWindowing.NyTz,
                        baselineExitDayKeyUtc: out _)));

            Assert.Equal(dsA.Samples.Count, dsB.Samples.Count);

            for (int i = 0; i < dsA.Samples.Count; i++)
            {
                var a = dsA.Samples[i];
                var b = dsB.Samples[i];

                Assert.Equal(a.EntryUtc, b.EntryUtc);
                Assert.Equal(a.Label, b.Label);

                Assert.NotNull(a.Features);
                Assert.NotNull(b.Features);

                Assert.Equal(SlSchema.FeatureCount, a.Features.Length);
                Assert.Equal(SlSchema.FeatureCount, b.Features.Length);

                for (int j = 0; j < a.Features.Length; j++)
                    Assert.Equal(a.Features[j], b.Features[j]);
            }
        }

        private static List<BacktestRecord> BuildSyntheticRows(
            int count,
            out Dictionary<DateTime, Candle6h> sol6hDict,
            out List<Candle1h> sol1h,
            out List<Candle1m> sol1m)
        {
            var rows = new List<BacktestRecord>(count);
            var dict6h = new Dictionary<DateTime, Candle6h>(count);
            var all1m = new List<Candle1m>(count * 30);

            var entriesUtc = NyTestDates.BuildNyWeekdaySeriesUtc(
                startNyLocalDate: NyTestDates.NyLocal(2022, 4, 1, 0),
                count: count,
                hour: 8);

            for (int i = 0; rows.Count < count; i++)
            {
                var t = entriesUtc[i];
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
                    Open = price,
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
            sol1h = BuildHourlySeries(entriesUtc, basePrice: 100.0);
            sol1m = all1m;

            return rows.OrderBy(r => r.Causal.EntryDayKeyUtc.Value).ToList();
        }

        private static BacktestRecord CreateBacktestRecord(NyTradingEntryUtc entryUtc, bool isMorning, double minMove)
        {
            var rawEntryUtc = entryUtc.AsEntryUtc();
            var vec = BuildVector64Deterministic(rawEntryUtc.Value);

            var causal = new CausalPredictionRecord
            {
                TradingEntryUtc = entryUtc,
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
                EntryUtc = rawEntryUtc,
                WindowEndUtc = NyWindowing.ComputeBaselineExitUtc(rawEntryUtc, NyWindowing.NyTz).Value,
                Entry = 100.0,
                MaxHigh24 = 105.0,
                MinLow24 = 95.0,
                Close24 = 100.0,
                DayMinutes = Array.Empty<Candle1m>(),
                MinMove = minMove,
                TrueLabel = 1,
                MicroTruth = OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.MicroNeutral)
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
                    entryUtc: r.Causal.TradingEntryUtc,
                    isMorning: r.Causal.IsMorning == true,
                    minMove: r.Causal.MinMove));
            }
            return res;
        }

        private static List<Candle1h> BuildHourlySeries(IReadOnlyList<DateTime> entriesUtc, double basePrice)
        {
            if (entriesUtc.Count == 0)
                return new List<Candle1h>();

            var first = entriesUtc[0].AddHours(-24);
            var last = entriesUtc[^1].AddHours(1);

            var list = new List<Candle1h>();
            for (var t = first; t <= last; t = t.AddHours(1))
            {
                list.Add(new Candle1h
                {
                    OpenTimeUtc = t,
                    Open = basePrice,
                    Close = basePrice,
                    High = basePrice * 1.01,
                    Low = basePrice * 0.99
                });
            }

            return list;
        }

        private static List<BacktestRecord> MutateFutureTail(List<BacktestRecord> rows, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc)
        {
            var res = new List<BacktestRecord>(rows.Count);

            foreach (var r in rows)
            {
                var cls = NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: r.Causal.EntryUtc,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyWindowing.NyTz,
                    baselineExitDayKeyUtc: out _);

                if (cls == NyTrainSplit.EntryClass.Train)
                {
                    res.Add(r);
                    continue;
                }

                res.Add(CreateBacktestRecord(
                    entryUtc: r.Causal.TradingEntryUtc,
                    isMorning: !(r.Causal.IsMorning == true),
                    minMove: r.Causal.MinMove * 2.0));
            }

            return res;
        }
    }
}

