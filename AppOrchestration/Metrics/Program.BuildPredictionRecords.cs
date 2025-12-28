using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

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
            List<Candle1m> sol1m
        )
        {
            // Локальный хелпер: печатает диапазон дат и распределение по day-of-week.
            // selector обязан возвращать "дневной ключ" (EntryDayKeyUtc) или другой стабильный идентификатор.
            static void DumpRange<T>(string label, IReadOnlyList<T> items, Func<T, EntryDayKeyUtc> selector)
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
            // Берём EntryDayKeyUtc, а не EntryUtc: это "идентичность дня", а не timestamp.
            DumpRange("mornings", mornings, r => CausalTimeKey.EntryDayKeyUtc(r));

            // PredictionEngine создаётся один раз на весь проход, чтобы не пересоздавать модель на каждый день.
            var engine = CreatePredictionEngineOrFallback(allRows);

            // Строим BacktestRecord по утренним точкам:
            // - causal прогнозы,
            // - forward-метрики и baseline 1m-окно.
            var records = await LoadPredictionRecordsAsync(mornings, sol1m, engine);

            // Логируем результат по day-key (стабильное сопоставление "дней").
            DumpRange("mornings", mornings, r => CausalTimeKey.EntryDayKeyUtc(r));

            // Диагностика: распределение предиктов на train/oos (использует внутренние правила разбиения).
            DumpRange("records", records, r => CausalTimeKey.EntryDayKeyUtc(r));

            Console.WriteLine($"[records] built = {records.Count}");

            return records;
        }
    }
}
