using System;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data
{
    /// <summary>
    /// Факт исполнения delayed-входа (A/B).
    ///
    /// Контракт:
    /// - Отсутствие исполнения выражается null на уровне BacktestRecord.DelayedExecution.
    /// - ExecutedAtUtc.Kind == Utc.
    /// - EntryPrice конечен и > 0.
    /// </summary>
    public sealed record DelayedExecutionFacts
    {
        public required DateTime ExecutedAtUtc { get; init; }
        public required double EntryPrice { get; init; }
        public required DelayedIntradayResult IntradayResult { get; init; }

        public static DelayedExecutionFacts Create(
            DateTime executedAtUtc,
            double entryPrice,
            DelayedIntradayResult intradayResult)
        {
            // Запрет non-UTC: таймстамп должен быть детерминирован для окон/границ.
            if (executedAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new InvalidOperationException(
                    $"[delayed] ExecutedAtUtc must be UTC, got Kind={executedAtUtc.Kind}, value={executedAtUtc:O}.");
            }

            // Запрет NaN/Infinity/<=0: “0 как заглушка” недопустим.
            if (double.IsNaN(entryPrice) || double.IsInfinity(entryPrice) || entryPrice <= 0.0)
            {
                throw new InvalidOperationException(
                    $"[delayed] EntryPrice must be finite and > 0, got {entryPrice}.");
            }

            // Создание immutable факта исполнения.
            return new DelayedExecutionFacts
            {
                ExecutedAtUtc = executedAtUtc,
                EntryPrice = entryPrice,
                IntradayResult = intradayResult
            };
        }
    }
}
