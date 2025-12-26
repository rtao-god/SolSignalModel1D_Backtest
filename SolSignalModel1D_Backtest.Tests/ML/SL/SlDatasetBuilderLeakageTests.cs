using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using CoreSlOfflineBuilder = SolSignalModel1D_Backtest.Tests.Data.NyWindowing.ComputeBaselineExitUtc.SlOfflineBuilder;
using System.Collections.Generic;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.SL
{
    /// <summary>
    /// Тесты, которые проверяют, что SlDatasetBuilder не допускает утечек:
    /// - SL-сэмплы строятся по 1m-пути,
    /// - но в Train датасет попадают только дни, у которых baseline-exit day-key <= trainUntilExitDayKeyUtc.
    /// </summary>
    public sealed class SlDatasetBuilderLeakageTests
    {
        [Fact]
        public void Build_DropsSamplesWhoseBaselineExitGoesBeyondTrainUntil()
        {
            var nyTz = TimeZones.NewYork;

            // Будний день, NY-утро.
            var entryLocalNy = new DateTime(2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified);
            var entryUtcDt = TimeZoneInfo.ConvertTimeToUtc(entryLocalNy, nyTz);
            var entry = new EntryUtc(entryUtcDt);

            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entry, nyTz);
            Assert.True(exitUtc.Value > entryUtcDt);

            // 1m-окно (короткое, но достаточное для срабатывания TP/SL в первой минуте).
            var sol1m = new List<Candle1m>();
            double entryPrice = 100.0;
            double tpPct = 0.01;
            double slPct = 0.02;

            for (int i = 0; i < 10; i++)
            {
                var t = entryUtcDt.AddMinutes(i);

                sol1m.Add(new Candle1m
                {
                    OpenTimeUtc = t,
                    High = entryPrice * (1.0 + tpPct + 0.01),
                    Low = entryPrice * (1.0 - slPct - 0.01),
                    Close = entryPrice
                });
            }

            // 1h история нужна, потому что SlFeatureBuilder строит фичи из 1h.
            var sol1h = new List<Candle1h>();
            var hStart = entryUtcDt.AddDays(-7);
            var hEnd = exitUtc.Value.AddHours(2);

            for (var t = hStart; t < hEnd; t = t.AddHours(1))
            {
                sol1h.Add(new Candle1h
                {
                    OpenTimeUtc = t,
                    Open = entryPrice,
                    High = entryPrice * 1.001,
                    Low = entryPrice * 0.999,
                    Close = entryPrice
                });
            }

            // Один утренний BacktestRecord.
            var rows = new List<BacktestRecord>
            {
                new BacktestRecord
                {
                    Causal = new CausalPredictionRecord
                    {
                        EntryUtc = entry,
                        MinMove = 0.02,
                        PredLabel = 2,
                        PredMicroUp = false,
                        PredMicroDown = false
                    },
                    Forward = new ForwardOutcomes
                    {
                        TrueLabel = 2,
                        FactMicroUp = false,
                        FactMicroDown = false,

                        Entry = entryPrice,
                        MaxHigh24 = entryPrice,
                        MinLow24 = entryPrice,
                        Close24 = entryPrice,

                        MinMove = 0.02,
                        WindowEndUtc = exitUtc.Value,

                        DayMinutes = Array.Empty<Candle1m>()
                    }
                }
            };

            var sol6hDict = new Dictionary<DateTime, Candle6h>
            {
                [entryUtcDt] = new Candle6h
                {
                    OpenTimeUtc = entryUtcDt,
                    Open = entryPrice,
                    High = entryPrice,
                    Low = entryPrice,
                    Close = entryPrice
                }
            };

            // Raw SL-сэмплы напрямую через SlOfflineBuilder: без фильтра по boundary они должны строиться.
            var rawSamples = CoreSlOfflineBuilder.Build(
                 rows: rows,
                 sol1h: sol1h,
                 sol1m: sol1m,
                 sol6hDict: sol6hDict,
                 tpPct: tpPct,
                 slPct: slPct,
                 strongSelector: null);

            Assert.NotEmpty(rawSamples);
            Assert.All(rawSamples, s => Assert.Equal(entryUtcDt, s.EntryUtc));

            // Граница ставится на день ДО exit-day-key => этот день должен попасть в OOS, а train-сэмплы быть пустыми.
            var exitDayKeyUtc = DayKeyUtc.FromUtcMomentOrThrow(exitUtc.Value);
            var trainUntilExitDayKeyUtc = DayKeyUtc.FromUtcOrThrow(exitDayKeyUtc.Value.AddDays(-1));

            var ds = SlDatasetBuilder.Build(
                rows: rows,
                sol1h: sol1h,
                sol1m: sol1m,
                sol6hDict: sol6hDict,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                tpPct: tpPct,
                slPct: slPct,
                strongSelector: null);

            Assert.Empty(ds.Samples);
            Assert.Empty(ds.MorningRows);
        }
    }
}
