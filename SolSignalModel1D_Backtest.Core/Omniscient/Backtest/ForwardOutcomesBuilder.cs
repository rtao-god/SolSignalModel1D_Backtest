using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
{
    /// <summary>
    /// Строит omniscient BacktestRecord из:
    /// - каузальных прогнозов (CausalPredictionRecord),
    /// - truth (LabeledCausalRow),
    /// - полного ряда 1m-свечей (forward-факты).
    ///
    /// Контракт времени:
    /// - EntryUtc: момент старта окна (утро/вход) — используется для нарезки минут и NyWindowing.ComputeBaselineExitUtc.
    /// - DayKeyUtc: ключ дня (00:00 UTC) — используется для "идентичности дня" и сопоставления truth↔causal.
    ///
    /// Инварианты данных:
    /// - allMinutes строго отсортирован по OpenTimeUtc (UTC) и без дублей;
    /// - OHLC валидны (finite, > 0, High>=Low, High покрывает Open/Close, Low покрывает Open/Close).
    /// </summary>
    public static class ForwardOutcomesBuilder
    {
        public static IReadOnlyList<BacktestRecord> Build(
            IReadOnlyList<CausalPredictionRecord> causalRecords,
            IReadOnlyList<LabeledCausalRow> truthRows,
            IReadOnlyList<Candle1m> allMinutes)
        {
            // Базовая валидация входов.
            if (causalRecords == null) throw new ArgumentNullException(nameof(causalRecords));
            if (truthRows == null) throw new ArgumentNullException(nameof(truthRows));
            if (allMinutes == null) throw new ArgumentNullException(nameof(allMinutes));

            // Пустой набор прогнозов -> пустой результат, без ошибок.
            if (causalRecords.Count == 0)
                return Array.Empty<BacktestRecord>();

            // Минуты обязательны: без них нельзя получить forward-исходы.
            if (allMinutes.Count == 0)
                throw new InvalidOperationException("[forward] 1m-ряд пуст: невозможно посчитать forward-исходы.");

            // Глобальная гарантия монотонности минут.
            SeriesGuards.EnsureStrictlyAscendingUtc(allMinutes, m => m.OpenTimeUtc, "ForwardOutcomesBuilder.allMinutes");

            // Индексируем truth по DayKeyUtc, чтобы сопоставление было устойчивым к любым "timestamp-деталям".
            // Дополнительно ниже проверяем, что EntryUtc совпал 1:1 (если нет — это рассинхрон пайплайна).
            var truthByDayKey = new Dictionary<DateTime, LabeledCausalRow>(truthRows.Count);
            for (int i = 0; i < truthRows.Count; i++)
            {
                var t = truthRows[i];

                // Ключ дня всегда должен быть UTC 00:00.
                var dayKeyUtc = t.DayKeyUtc;
                if (dayKeyUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[forward] truth.DayKeyUtc must be UTC: {dayKeyUtc:O}.");

                // Дубликаты по дню означают повреждение/рассинхрон входных данных.
                if (!truthByDayKey.TryAdd(dayKeyUtc, t))
                    throw new InvalidOperationException($"[forward] duplicate truth row for dayKey {dayKeyUtc:O}.");
            }

            // Стабильный порядок обработки дней: по EntryUtc (реальный момент входа).
            var orderedRecords = causalRecords
                .OrderBy(r => r.EntryUtc)
                .ToList();

            var result = new List<BacktestRecord>(orderedRecords.Count);

            // Скользящий индекс по минутам:
            // предполагается, что окна идут в возрастающем времени и не "ездят назад".
            int minuteIndex = 0;

            foreach (var causal in orderedRecords)
            {
                // EntryUtc — фактическое начало окна (момент входа).
                var entryUtc = causal.EntryUtc;

                // DayKeyUtc — идентичность дня (00:00 UTC).
                var dayKeyUtc = causal.DayKeyUtc;

                // Каузальная запись обязана быть UTC timestamp.
                if (entryUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[forward] causal.EntryUtc must be UTC: {entryUtc:O}.");

                // Берём truth по ключу дня.
                if (!truthByDayKey.TryGetValue(dayKeyUtc, out var truth))
                    throw new InvalidOperationException($"[forward] No truth row for causal dayKey {dayKeyUtc:O} (entry={entryUtc:O}).");

                // Жёсткая проверка на рассинхрон момента входа:
                // если EntryUtc отличается — значит causal и truth описывают разные "утра".
                if (truth.EntryUtc != entryUtc)
                {
                    throw new InvalidOperationException(
                        $"[forward] truth.EntryUtc != causal.EntryUtc for dayKey {dayKeyUtc:O}. " +
                        $"truthEntry={truth.EntryUtc:O}, causalEntry={entryUtc:O}.");
                }

                // Конец окна по time-contract NyWindowing (полуоткрытое окно [entryUtc; windowEndUtc)).
                var windowEndUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz);

                // Окно обязано быть валидным и иметь положительную длительность.
                if (windowEndUtc <= entryUtc)
                    throw new InvalidOperationException(
                        $"[forward] Invalid baseline window: entry={entryUtc:O}, end={windowEndUtc:O}, dayKey={dayKeyUtc:O}.");

                // Сдвигаем minuteIndex до первой минуты окна: OpenTimeUtc >= entryUtc.
                while (minuteIndex < allMinutes.Count && allMinutes[minuteIndex].OpenTimeUtc < entryUtc)
                    minuteIndex++;

                // Собираем минуты окна в отдельный список.
                // Здесь не делается никаких сортировок/фильтров по "дням", только по реальному временному окну.
                var dayMinutes = new List<Candle1m>();
                int j = minuteIndex;

                // Полуоткрытое окно [entryUtc; windowEndUtc).
                while (j < allMinutes.Count)
                {
                    var m = allMinutes[j];

                    // Как только вышли за конец окна — останавливаемся.
                    if (m.OpenTimeUtc >= windowEndUtc)
                        break;

                    dayMinutes.Add(m);
                    j++;
                }

                // Пустое окно означает "нет минут" для данного entryUtc.
                if (dayMinutes.Count == 0)
                    throw new InvalidOperationException(
                        $"[forward] No 1m candles found for window start {entryUtc:O} (end={windowEndUtc:O}, dayKey={dayKeyUtc:O}).");

                // Следующий день начинаем читать минуты с конца текущего окна.
                minuteIndex = j;

                // Строгая валидация минут окна: UTC + валидный OHLC.
                for (int k = 0; k < dayMinutes.Count; k++)
                    ValidateMinuteCandle(dayMinutes[k], entryUtc);

                // Entry-цена: используем Open первой минуты окна.
                var first = dayMinutes[0];
                if (!double.IsFinite(first.Open) || first.Open <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Entry price must be finite and > 0 (entry={entryUtc:O}, dayKey={dayKeyUtc:O}).");

                double entry = first.Open;

                // Проходим окно и снимаем экстремумы.
                double maxHigh = double.NegativeInfinity;
                double minLow = double.PositiveInfinity;

                for (int k = 0; k < dayMinutes.Count; k++)
                {
                    var m = dayMinutes[k];
                    if (m.High > maxHigh) maxHigh = m.High;
                    if (m.Low < minLow) minLow = m.Low;
                }

                // Базовые проверки экстремумов.
                if (!double.IsFinite(maxHigh) || maxHigh <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Invalid maxHigh in window (entry={entryUtc:O}, dayKey={dayKeyUtc:O}).");

                if (!double.IsFinite(minLow) || minLow <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Invalid minLow in window (entry={entryUtc:O}, dayKey={dayKeyUtc:O}).");

                // Close окна: Close последней минуты.
                var last = dayMinutes[dayMinutes.Count - 1];
                if (!double.IsFinite(last.Close) || last.Close <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Last Close must be finite and > 0 (entry={entryUtc:O}, dayKey={dayKeyUtc:O}).");

                double close24 = last.Close;

                // Проверяем, что экстремумы совместимы с entry (иначе это неконсистентность данных).
                double upDiff = maxHigh - entry;
                if (upDiff < 0.0)
                    throw new InvalidOperationException(
                        $"[forward] maxHigh < entry: entry={entry:0.########}, maxHigh={maxHigh:0.########}, entryUtc={entryUtc:O}, dayKey={dayKeyUtc:O}.");

                double downDiff = entry - minLow;
                if (downDiff < 0.0)
                    throw new InvalidOperationException(
                        $"[forward] minLow > entry: entry={entry:0.########}, minLow={minLow:0.########}, entryUtc={entryUtc:O}, dayKey={dayKeyUtc:O}.");

                // Нормируем движения относительно entry.
                double upMove = upDiff / entry;
                double downMove = downDiff / entry;

                if (!double.IsFinite(upMove) || !double.IsFinite(downMove))
                    throw new InvalidOperationException(
                        $"[forward] Non-finite move computed (entryUtc={entryUtc:O}, dayKey={dayKeyUtc:O}).");

                // Итоговая амплитуда окна: максимум из |up| и |down|.
                double forwardMinMove = Math.Max(upMove, downMove);

                // ForwardOutcomes: складываем forward-факты.
                // Важно: DateUtc здесь трактуем как DayKeyUtc (идентичность дня),
                // а момент входа хранится в BacktestRecord.Causal.EntryUtc.
                var forward = new ForwardOutcomes
                {
                    DateUtc = dayKeyUtc,
                    WindowEndUtc = windowEndUtc,
                    Entry = entry,
                    MaxHigh24 = maxHigh,
                    MinLow24 = minLow,
                    Close24 = close24,
                    DayMinutes = dayMinutes,
                    MinMove = forwardMinMove,

                    TrueLabel = truth.TrueLabel,
                    FactMicroUp = truth.FactMicroUp,
                    FactMicroDown = truth.FactMicroDown
                };

                // BacktestRecord: каузальная часть + forward-факты.
                result.Add(new BacktestRecord
                {
                    Causal = causal,
                    Forward = forward
                });
            }

            return result;
        }

        private static void ValidateMinuteCandle(Candle1m m, DateTime entryUtc)
        {
            // Время свечи обязано быть UTC.
            if (m.OpenTimeUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[forward] Candle1m.OpenTimeUtc must be UTC (entry {entryUtc:O}).");

            // Цена обязана быть finite и > 0.
            ValidatePrice(m.Open, "Open", entryUtc);
            ValidatePrice(m.High, "High", entryUtc);
            ValidatePrice(m.Low, "Low", entryUtc);
            ValidatePrice(m.Close, "Close", entryUtc);

            // Базовая консистентность диапазона.
            if (m.High < m.Low)
                throw new InvalidOperationException($"[forward] Candle1m has High < Low (entry {entryUtc:O}).");

            // High должен быть >= Open и >= Close.
            if (m.High < m.Open || m.High < m.Close)
                throw new InvalidOperationException($"[forward] Candle1m has High below Open/Close (entry {entryUtc:O}).");

            // Low должен быть <= Open и <= Close.
            if (m.Low > m.Open || m.Low > m.Close)
                throw new InvalidOperationException($"[forward] Candle1m has Low above Open/Close (entry {entryUtc:O}).");
        }

        private static void ValidatePrice(double v, string name, DateTime entryUtc)
        {
            if (!double.IsFinite(v) || v <= 0.0)
                throw new InvalidOperationException($"[forward] Candle1m.{name} must be finite and > 0 (entry {entryUtc:O}).");
        }
    }
}
