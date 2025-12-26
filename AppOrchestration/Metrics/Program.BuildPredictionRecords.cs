using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        /// <summary>
        /// Строит записи прогнозов дневной модели по утренним точкам.
        /// Здесь инкапсулируется выбор PredictionEngine и сбор forward-метрик.
        /// </summary>
        private static async Task<List<BacktestRecord>> BuildPredictionRecordsAsync(
            List<LabeledCausalRow> allRows,
            List<LabeledCausalRow> mornings,
            List<Candle6h> solAll6h
        )
        {
            // Локальный хелпер: печатает диапазон дат и распределение по day-of-week.
            // selector обязан возвращать "дневной ключ" (DayKeyUtc) или другой стабильный идентификатор.
            static void DumpRange<T>(string label, IReadOnlyList<T> items, Func<T, DayKeyUtc> selector)
            {
                if (items == null || items.Count == 0)
                {
                    Console.WriteLine($"[{label}] empty");
                    return;
                }

                var keys = items.Select(selector).ToList();

                var min = keys.Min(k => k.Value);
                var max = keys.Max(k => k.Value);

                Console.WriteLine(
                    $"[{label}] range = [{min:yyyy-MM-dd} ({min.DayOfWeek}); {max:yyyy-MM-dd} ({max.DayOfWeek})], count={items.Count}");

                var dowHist = keys
                    .Select(k => k.Value)
                    .GroupBy(d => d.DayOfWeek)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}={g.Count()}")
                    .ToArray();

                Console.WriteLine($"[{label}] DayOfWeek hist: {string.Join(", ", dowHist)}");
            }

            // Логируем, какие дни реально присутствуют в утренних точках.
            // Берём DayKeyUtc, а не EntryUtc: это "идентичность дня", а не timestamp.
            DumpRange("mornings", mornings, r => CausalTimeKey.DayKeyUtc(r));

            // PredictionEngine создаётся один раз на весь проход, чтобы не пересоздавать модель на каждый день.
            var engine = CreatePredictionEngineOrFallback(allRows);

            // Строим BacktestRecord по утренним точкам:
            // - causal прогнозы,
            // - forward-метрики (на основе solAll6h и внутренней логики загрузчика).
            var records = await LoadPredictionRecordsAsync(mornings, solAll6h, engine);

            // Логируем результат по day-key (стабильное сопоставление "дней").
            DumpRange("mornings", mornings, r => CausalTimeKey.DayKeyUtc(r));

            // Диагностика: распределение предиктов на train/oos (использует внутренние правила разбиения).
            DumpRange("records", records, r => CausalTimeKey.DayKeyUtc(r));

            Console.WriteLine($"[records] built = {records.Count}");

            return records;
        }
    }
}
