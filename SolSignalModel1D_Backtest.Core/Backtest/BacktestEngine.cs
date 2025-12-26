using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Backtest
{
    /// <summary>
    /// Универсальный движок бэктеста:
    /// принимает подготовленные данные и BacktestConfig,
    /// прогоняет политики (BASE/ANTI-D × WITH SL / NO SL) через PnL-движок,
    /// возвращает BacktestSummary.
    /// </summary>
    public static class BacktestEngine
    {
        /// <summary>
        /// Выполняет one-shot бэктест по заданному конфигу и данным.
        /// Данные (mornings/records/candles1m/policies) ожидаются уже подготовленными.
        /// </summary>
        public static BacktestSummary RunBacktest(
            IReadOnlyList<LabeledCausalRow> mornings,
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> candles1m,
            IReadOnlyList<RollingLoop.PolicySpec> policies,
            BacktestConfig config)
        {
            // Валидация входов: движок не делает "пустые" прогоны.
            if (mornings == null || mornings.Count == 0)
                throw new ArgumentException("mornings must be non-empty", nameof(mornings));
            if (records == null || records.Count == 0)
                throw new ArgumentException("records must be non-empty", nameof(records));
            if (candles1m == null || candles1m.Count == 0)
                throw new ArgumentException("candles1m must be non-empty", nameof(candles1m));
            if (policies == null || policies.Count == 0)
                throw new ArgumentException("policies must be non-empty", nameof(policies));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // Диапазон дат в summary считаем по day-key, чтобы это была "шкала дней", а не timestamp.
            // Это важно, чтобы даты summary были устойчивы и не зависели от "времени входа".
            var fromDate = mornings.Min(r => CausalTimeKey.DayKeyUtc(r));
            var toDate = mornings.Max(r => CausalTimeKey.DayKeyUtc(r));

            // Прогоняем все четыре режима:
            // - useStopLoss: влияет на daily SL + delayed intraday stops
            // - useAnti: включает anti-direction overlay
            var withSlBase = SimulateAllPolicies(policies, records, candles1m, useStopLoss: true, config: config, useAnti: false);
            var noSlBase = SimulateAllPolicies(policies, records, candles1m, useStopLoss: false, config: config, useAnti: false);
            var withSlAnti = SimulateAllPolicies(policies, records, candles1m, useStopLoss: true, config: config, useAnti: true);
            var noSlAnti = SimulateAllPolicies(policies, records, candles1m, useStopLoss: false, config: config, useAnti: true);

            // Агрегаты по всем результатам (для быстрых summary-метрик).
            double bestTotalPnl = double.NegativeInfinity;
            double worstMaxDd = double.NegativeInfinity;
            int policiesWithLiq = 0;
            int totalTrades = 0;

            // Аккумулируем метрики по каждому набору результатов.
            void Accumulate(IEnumerable<BacktestPolicyResult> src)
            {
                foreach (var r in src)
                {
                    // Счётчик сделок: учитываем только если список реально присутствует.
                    if (r.Trades != null)
                        totalTrades += r.Trades.Count;

                    // Лучший total pnl по всем политикам/режимам.
                    if (r.TotalPnlPct > bestTotalPnl)
                        bestTotalPnl = r.TotalPnlPct;

                    // "WorstMaxDdPct" здесь исторически хранит максимум maxDD (как агрегат).
                    if (r.MaxDdPct > worstMaxDd)
                        worstMaxDd = r.MaxDdPct;

                    // Сколько политик ловили ликвидацию.
                    if (r.HadLiquidation)
                        policiesWithLiq++;
                }
            }

            Accumulate(withSlBase);
            Accumulate(noSlBase);
            Accumulate(withSlAnti);
            Accumulate(noSlAnti);

            // Защитные значения, если вдруг набор результатов оказался пустым (например, все Policy=null).
            if (double.IsNegativeInfinity(bestTotalPnl))
                bestTotalPnl = 0.0;
            if (double.IsNegativeInfinity(worstMaxDd))
                worstMaxDd = 0.0;

            var fromDayKey = mornings.Min(r => CausalTimeKey.DayKeyUtc(r));
            var toDayKey = mornings.Max(r => CausalTimeKey.DayKeyUtc(r));

            // Формируем итоговый summary.
            return new BacktestSummary
            {
                Config = config,
                FromDateUtc = fromDayKey.Value,
                ToDateUtc = toDayKey.Value,
                SignalDays = mornings.Count,

                WithSlBase = withSlBase,
                NoSlBase = noSlBase,
                WithSlAnti = withSlAnti,
                NoSlAnti = noSlAnti,

                BestTotalPnlPct = bestTotalPnl,
                WorstMaxDdPct = worstMaxDd,
                PoliciesWithLiquidation = policiesWithLiq,
                TotalTrades = totalTrades
            };
        }

        private static List<BacktestPolicyResult> SimulateAllPolicies(
            IReadOnlyList<RollingLoop.PolicySpec> policies,
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> candles1m,
            bool useStopLoss,
            BacktestConfig config,
            bool useAnti)
        {
            // Список результатов: один элемент на политику.
            var results = new List<BacktestPolicyResult>(policies.Count);

            foreach (var p in policies)
            {
                // Исторически пропускаем пустые спеки.
                if (p.Policy == null) continue;

                // PnL-движок делает всю тяжёлую работу и возвращает набор выходных артефактов.
                PnlCalculator.ComputePnL(
                    records,
                    p.Policy,
                    p.Margin,
                    out var trades,
                    out var totalPnlPct,
                    out var maxDdPct,
                    out var tradesBySource,
                    out var withdrawnTotal,
                    out var bucketSnapshots,
                    out var hadLiquidation,
                    useDailyStopLoss: useStopLoss,
                    useDelayedIntradayStops: useStopLoss,
                    dailyTpPct: config.DailyTpPct,
                    dailyStopPct: config.DailyStopPct,
                    useAntiDirectionOverlay: useAnti,
                    predictionMode: PnlPredictionMode.DayOnly
                );

                // Укладываем результат политики в DTO.
                results.Add(new BacktestPolicyResult
                {
                    PolicyName = p.Name,
                    Margin = p.Margin,
                    Trades = trades,
                    TotalPnlPct = totalPnlPct,
                    MaxDdPct = maxDdPct,
                    TradesBySource = tradesBySource,
                    WithdrawnTotal = withdrawnTotal,
                    BucketSnapshots = bucketSnapshots,
                    HadLiquidation = hadLiquidation
                });
            }

            // Стабильный порядок для вывода/сравнения.
            return results
                .OrderBy(r => r.PolicyName)
                .ThenBy(r => r.Margin.ToString())
                .ToList();
        }
    }
}
