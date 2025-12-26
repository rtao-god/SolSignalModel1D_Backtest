using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
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
    /// - EntryUtc: старт baseline-окна (утро/вход);
    /// - EntryDayKeyUtc: идентичность дня записи (00:00 UTC) для truth↔causal.
    /// </summary>
    public static class ForwardOutcomesBuilder
    {
        public static IReadOnlyList<BacktestRecord> Build(
            IReadOnlyList<CausalPredictionRecord> causalRecords,
            IReadOnlyList<LabeledCausalRow> truthRows,
            IReadOnlyList<Candle1m> allMinutes)
        {
            if (causalRecords == null) throw new ArgumentNullException(nameof(causalRecords));
            if (truthRows == null) throw new ArgumentNullException(nameof(truthRows));
            if (allMinutes == null) throw new ArgumentNullException(nameof(allMinutes));

            if (causalRecords.Count == 0)
                return Array.Empty<BacktestRecord>();

            if (allMinutes.Count == 0)
                throw new InvalidOperationException("[forward] 1m-ряд пуст: невозможно посчитать forward-исходы.");

            SeriesGuards.EnsureStrictlyAscendingUtc(allMinutes, m => m.OpenTimeUtc, "ForwardOutcomesBuilder.allMinutes");

            // truth индексируем по entry-day-key (EntryDayKeyUtc).
            var truthByDayKey = new Dictionary<EntryDayKeyUtc, LabeledCausalRow>(truthRows.Count);
            for (int i = 0; i < truthRows.Count; i++)
            {
                var t = truthRows[i];

                var dayKeyUtc = t.Causal.EntryDayKeyUtc;
                if (dayKeyUtc.IsDefault)
                    throw new InvalidOperationException("[forward] truth row has default entry day-key.");

                if (!truthByDayKey.TryAdd(dayKeyUtc, t))
                    throw new InvalidOperationException($"[forward] duplicate truth row for dayKey {dayKeyUtc.Value:O}.");
            }

            var orderedRecords = causalRecords
                .OrderBy(r => r.EntryUtc.Value)
                .ToList();

            var result = new List<BacktestRecord>(orderedRecords.Count);

            int minuteIndex = 0;

            foreach (var causal in orderedRecords)
            {
                DateTime entryUtcRaw = causal.EntryUtc.Value;

                var dayKeyUtc = causal.EntryDayKeyUtc;
                if (dayKeyUtc.IsDefault)
                    throw new InvalidOperationException($"[forward] causal record has default entry day-key (entry={entryUtcRaw:O}).");

                if (!truthByDayKey.TryGetValue(dayKeyUtc, out var truth))
                    throw new InvalidOperationException($"[forward] No truth row for causal dayKey {dayKeyUtc.Value:O} (entry={entryUtcRaw:O}).");

                var truthEntryUtc = truth.Causal.EntryUtc.Value;
                if (truthEntryUtc != entryUtcRaw)
                {
                    throw new InvalidOperationException(
                        $"[forward] truth.EntryUtc != causal.EntryUtc for dayKey {dayKeyUtc.Value:O}. " +
                        $"truthEntry={truthEntryUtc:O}, causalEntry={entryUtcRaw:O}.");
                }

                var entryUtc = new EntryUtc(entryUtcRaw);

                DateTime windowEndUtc = NyWindowing
                    .ComputeBaselineExitUtc(entryUtc, NyWindowing.NyTz)
                    .Value;

                if (windowEndUtc <= entryUtcRaw)
                    throw new InvalidOperationException(
                        $"[forward] Invalid baseline window: entry={entryUtcRaw:O}, end={windowEndUtc:O}, dayKey={dayKeyUtc.Value:O}.");

                while (minuteIndex < allMinutes.Count && allMinutes[minuteIndex].OpenTimeUtc < entryUtcRaw)
                    minuteIndex++;

                var dayMinutes = new List<Candle1m>();
                int j = minuteIndex;

                while (j < allMinutes.Count)
                {
                    var m = allMinutes[j];
                    if (m.OpenTimeUtc >= windowEndUtc)
                        break;

                    dayMinutes.Add(m);
                    j++;
                }

                if (dayMinutes.Count == 0)
                    throw new InvalidOperationException(
                        $"[forward] No 1m candles found for window start {entryUtcRaw:O} (end={windowEndUtc:O}, dayKey={dayKeyUtc.Value:O}).");

                minuteIndex = j;

                for (int k = 0; k < dayMinutes.Count; k++)
                    ValidateMinuteCandle(dayMinutes[k], entryUtcRaw);

                var first = dayMinutes[0];
                if (!double.IsFinite(first.Open) || first.Open <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Entry price must be finite and > 0 (entry={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}).");

                double entry = first.Open;

                double maxHigh = double.NegativeInfinity;
                double minLow = double.PositiveInfinity;

                for (int k = 0; k < dayMinutes.Count; k++)
                {
                    var m = dayMinutes[k];
                    if (m.High > maxHigh) maxHigh = m.High;
                    if (m.Low < minLow) minLow = m.Low;
                }

                if (!double.IsFinite(maxHigh) || maxHigh <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Invalid maxHigh in window (entry={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}).");

                if (!double.IsFinite(minLow) || minLow <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Invalid minLow in window (entry={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}).");

                var last = dayMinutes[dayMinutes.Count - 1];
                if (!double.IsFinite(last.Close) || last.Close <= 0.0)
                    throw new InvalidOperationException(
                        $"[forward] Last Close must be finite and > 0 (entry={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}).");

                double close24 = last.Close;

                double upDiff = maxHigh - entry;
                if (upDiff < 0.0)
                    throw new InvalidOperationException(
                        $"[forward] maxHigh < entry: entry={entry:0.########}, maxHigh={maxHigh:0.########}, entryUtc={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}.");

                double downDiff = entry - minLow;
                if (downDiff < 0.0)
                    throw new InvalidOperationException(
                        $"[forward] minLow > entry: entry={entry:0.########}, minLow={minLow:0.########}, entryUtc={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}.");

                double upMove = upDiff / entry;
                double downMove = downDiff / entry;

                if (!double.IsFinite(upMove) || !double.IsFinite(downMove))
                    throw new InvalidOperationException(
                        $"[forward] Non-finite move computed (entryUtc={entryUtcRaw:O}, dayKey={dayKeyUtc.Value:O}).");

                double forwardMinMove = Math.Max(upMove, downMove);

                var forward = new ForwardOutcomes
                {
                    EntryUtc = entryUtc,
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
            if (m.OpenTimeUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[forward] Candle1m.OpenTimeUtc must be UTC (entry {entryUtc:O}).");

            ValidatePrice(m.Open, "Open", entryUtc);
            ValidatePrice(m.High, "High", entryUtc);
            ValidatePrice(m.Low, "Low", entryUtc);
            ValidatePrice(m.Close, "Close", entryUtc);

            if (m.High < m.Low)
                throw new InvalidOperationException($"[forward] Candle1m has High < Low (entry {entryUtc:O}).");

            if (m.High < m.Open || m.High < m.Close)
                throw new InvalidOperationException($"[forward] Candle1m has High below Open/Close (entry {entryUtc:O}).");

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
