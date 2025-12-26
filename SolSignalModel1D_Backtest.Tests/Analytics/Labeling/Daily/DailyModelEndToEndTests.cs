using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
{
    /// <summary>
    /// E2E-тесты для дневной модели:
    /// - синтетическая монотонная история (smoke: пайплайн не падает, классы валидные);
    /// - синтетическая зигзагообразная история (модель не должна вырождаться в константу).
    /// Пайплайн: RowBuilder.BuildDailyRows → ModelTrainer.TrainAll → PredictionEngine.
    /// </summary>
    public sealed class DailyModelEndToEndTests
    {
        private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

        [Fact]
        public void DailyModel_EndToEnd_OnMonotonicTrendHistory_ProducesValidPredictions()
        {
            var rows = BuildMonotonicHistory();
            var ordered = rows.OrderBy(EntryUtc).ToList();

            const int HoldoutDays = 60;
            var result = TrainAndPredict(ordered, HoldoutDays);

            Assert.True(result.TotalPredictions > 0, "PredictionEngine не выдал ни одного предсказания.");
            Assert.Equal(0, result.ClassesOutOfRange);
            Assert.InRange(result.PredictedClasses.Count, 1, 3);
        }

        [Fact]
        public void DailyModel_EndToEnd_OnZigZagHistory_UsesAtLeastTwoClasses()
        {
            var rows = BuildZigZagHistory();
            var ordered = rows.OrderBy(EntryUtc).ToList();

            const int HoldoutDays = 60;
            var result = TrainAndPredict(ordered, HoldoutDays);

            Assert.True(result.TotalPredictions > 0, "PredictionEngine не выдал ни одного предсказания.");
            Assert.Equal(0, result.ClassesOutOfRange);
            Assert.True(
                result.PredictedClasses.Count >= 2,
                "Дневная модель выродилась в константу по классам на зигзагообразной истории."
            );
        }

        private sealed class DailyE2eResult
        {
            public ModelBundle Bundle { get; init; } = null!;
            public int TotalPredictions { get; init; }
            public HashSet<int> PredictedClasses { get; init; } = new();
            public int ClassesOutOfRange { get; init; }
        }

        /// <summary>
        /// Общая часть: делим на train/OOS, тренируем дневной бандл и прогоняем PredictionEngine по всей истории.
        /// </summary>
        private static DailyE2eResult TrainAndPredict(List<LabeledCausalRow> orderedRows, int holdoutDays)
        {
            if (orderedRows == null) throw new ArgumentNullException(nameof(orderedRows));
            Assert.NotEmpty(orderedRows);
            if (holdoutDays <= 0) throw new ArgumentOutOfRangeException(nameof(holdoutDays));

            // Здесь entryUtc берём явно из causal-части.
            var minDayKey = DayKeyUtc(orderedRows.First());
            var maxDayKey = DayKeyUtc(orderedRows.Last());

            var maxEntryUtc = EntryUtc(orderedRows.Last());
            var trainUntilEntryUtc = maxEntryUtc.AddDays(-holdoutDays);

            var trainRows = orderedRows
                .Where(r => EntryUtc(r) <= trainUntilEntryUtc)
                .ToList();

            var oosRows = orderedRows
                .Where(r => EntryUtc(r) > trainUntilEntryUtc)
                .ToList();

            Assert.True(trainRows.Count > 50,
                $"Слишком мало train-дней для обучения (train={trainRows.Count}, диапазон {minDayKey:yyyy-MM-dd}..{trainUntilEntryUtc:yyyy-MM-dd}).");
            Assert.True(oosRows.Count > 10,
                $"Слишком мало OOS-дней для проверки (oos={oosRows.Count}, диапазон {trainUntilEntryUtc:yyyy-MM-dd}..{maxDayKey:yyyy-MM-dd}).");

            var trainer = new ModelTrainer();
            var bundle = trainer.TrainAll(trainRows);

            Assert.NotNull(bundle);
            Assert.NotNull(bundle.MlCtx);
            Assert.NotNull(bundle.MoveModel);

            var engine = new PredictionEngine(bundle);

            int totalPredictions = 0;
            int clsOutOfRange = 0;
            var classes = new HashSet<int>();

            foreach (var row in orderedRows)
            {
                // Инвариант: предиктим только causal-часть.
                var pred = engine.PredictCausal(row.Causal);

                totalPredictions++;
                classes.Add(pred.PredLabel);

                if (pred.PredLabel < 0 || pred.PredLabel > 2)
                    clsOutOfRange++;
            }

            return new DailyE2eResult
            {
                Bundle = bundle,
                TotalPredictions = totalPredictions,
                PredictedClasses = classes,
                ClassesOutOfRange = clsOutOfRange
            };
        }

        /// <summary>
        /// UTC-момент входа (для сортировки/holdout).
        /// </summary>
        private static DateTime EntryUtc(LabeledCausalRow r) => r.Causal.EntryUtc.Value;

        /// <summary>
        /// Day-key (00:00 UTC) для сообщений/диапазонов/словарей.
        /// </summary>
        private static DateTime DayKeyUtc(LabeledCausalRow r) => CausalTimeKey.DayKeyUtc(r).Value;

        private static List<LabeledCausalRow> BuildMonotonicHistory()
        {
            return BuildSyntheticRows(
                solPriceFunc: i =>
                {
                    double trend = 100.0 + 0.002 * i;
                    double noise = Math.Sin(i * 0.005) * 0.3;
                    return trend + noise;
                }
            );
        }

        private static List<LabeledCausalRow> BuildZigZagHistory()
        {
            return BuildSyntheticRows(
                solPriceFunc: i =>
                {
                    double basePrice = 100.0;
                    double wave = 10.0 * Math.Sin(i * 0.01);
                    double slowDrift = 0.0005 * i;
                    return basePrice + wave + slowDrift;
                }
            );
        }

        /// <summary>
        /// Общий конструктор синтетической истории:
        /// - генерирует 1m-ряд по заданной функции цены SOL;
        /// - агрегирует его в 6h-свечи SOL;
        /// - строит простые тренды для BTC/PAXG;
        /// - генерирует FNG/DXY (важно: ключи DateTimeKind.Utc и покрытие дат).
        /// </summary>
        private static List<LabeledCausalRow> BuildSyntheticRows(Func<int, double> solPriceFunc)
        {
            if (solPriceFunc == null) throw new ArgumentNullException(nameof(solPriceFunc));

            const int total6h = 1000;
            var start = new DateTime(2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

            int totalMinutes = total6h * 6 * 60;

            var solAll1m = new List<Candle1m>(totalMinutes);
            var solPrices = new double[totalMinutes];

            for (int i = 0; i < totalMinutes; i++)
            {
                var t = start.AddMinutes(i);
                double price = solPriceFunc(i);

                if (price <= 0.0)
                    throw new InvalidOperationException($"Synthetic SOL price is non-positive at i={i}, t={t:O}: {price}.");

                solPrices[i] = price;

                solAll1m.Add(new Candle1m
                {
                    OpenTimeUtc = t,
                    Close = price,
                    High = price + 0.0005,
                    Low = price - 0.0005
                });
            }

            var solAll6h = new List<Candle6h>(total6h);
            var btcAll6h = new List<Candle6h>(total6h);
            var paxgAll6h = new List<Candle6h>(total6h);

            for (int block = 0; block < total6h; block++)
            {
                int startIdx = block * 360;
                int endIdx = startIdx + 360 - 1;

                double high = double.MinValue;
                double low = double.MaxValue;

                for (int idx = startIdx; idx <= endIdx; idx++)
                {
                    double p = solPrices[idx];
                    if (p > high) high = p;
                    if (p < low) low = p;
                }

                double close = solPrices[endIdx];
                var t6 = start.AddHours(6 * block);

                solAll6h.Add(new Candle6h
                {
                    OpenTimeUtc = t6,
                    Close = close,
                    High = high,
                    Low = low
                });

                double btcPrice = 50.0 + 0.05 * block;
                double paxgPrice = 1500.0 + 0.02 * block;

                btcAll6h.Add(new Candle6h
                {
                    OpenTimeUtc = t6,
                    Close = btcPrice,
                    High = btcPrice + 1.0,
                    Low = btcPrice - 1.0
                });

                paxgAll6h.Add(new Candle6h
                {
                    OpenTimeUtc = t6,
                    Close = paxgPrice,
                    High = paxgPrice + 1.0,
                    Low = paxgPrice - 1.0
                });
            }

            var fng = new Dictionary<DateTime, double>();
            var dxy = new Dictionary<DateTime, double>();

            var firstDate = start.ToCausalDateUtc().AddDays(-200);
            var lastDate = start.ToCausalDateUtc().AddDays(400);

            for (var d = firstDate; d <= lastDate; d = d.AddDays(1))
            {
                var key = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
                fng[key] = 50;
                dxy[key] = 100.0;
            }

            var rows = RowBuilder.BuildDailyRows(
                solWinTrain: solAll6h,
                btcWinTrain: btcAll6h,
                paxgWinTrain: paxgAll6h,
                solAll6h: solAll6h,
                solAll1m: solAll1m,
                fngHistory: fng,
                dxySeries: dxy,
                extraDaily: null,
                nyTz: NyTz
            ).LabeledRows;

            Assert.NotEmpty(rows);
            return rows.OrderBy(EntryUtc).ToList();
        }
    }
}
