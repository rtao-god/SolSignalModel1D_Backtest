using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
{
    /// <summary>
    /// Симуляция сценарной стратегии на основе дневных прогнозов модели и минутных свечей.
    /// Ключевые моменты:
    /// - PredLabel определяет направление дня (up/flat → лонг, down → шорт);
    /// - риск в день = доля капитала из параметров (TotalRiskFractionPerTrade);
    /// - профит выводится, убытки уменьшают баланс;
    /// - сценарии:
    ///   1) базовый TP без хеджа;
    ///   2) хедж заработал и закрыл базу+хедж в плюс;
    ///   3) хедж выбило по SL, открыли вторую ногу по тренду;
    ///   4) хедж выбило, открыли вторую ногу и нас выбило по слабому откату.
    /// </summary>
    public static class StrategySimulator
    {
        /// <summary>
        /// Основной вход симулятора.
        /// Внешний код управляет только:
        /// - списком дней (mornings),
        /// - списком PredictionRecord,
        /// - минутными свечами,
        /// - набором параметров StrategyParameters.
        /// </summary>
        public static StrategyStats Run(
            IReadOnlyList<LabeledCausalRow> mornings,
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> candles1m,
            StrategyParameters parameters)
        {
            if (mornings == null) throw new ArgumentNullException(nameof(mornings));
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (candles1m == null) throw new ArgumentNullException(nameof(candles1m));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var stats = new StrategyStats();

            if (mornings.Count == 0 || records.Count == 0)
                return stats;

            int days = Math.Min(mornings.Count, records.Count);
            if (mornings.Count != records.Count)
            {
                Console.WriteLine(
                    $"[strategy] WARN: mornings={mornings.Count}, records={records.Count}, " +
                    $"использую первые {days} точек для симуляции.");
            }

            // Предрасчитываем доли стейка между базовой ногой и хеджем.
            double totalStakeShape = parameters.BaseStakeUsd + parameters.HedgeStakeUsd;
            if (totalStakeShape <= 0.0)
                throw new InvalidOperationException("[strategy] BaseStakeUsd + HedgeStakeUsd must be > 0.");

            double baseStakeFraction = parameters.BaseStakeUsd / totalStakeShape;
            double hedgeStakeFraction = parameters.HedgeStakeUsd / totalStakeShape;

            // Текущий баланс и базовые метрики по капиталу.
            double balance = parameters.InitialBalanceUsd;
            stats.StartBalance = balance;
            stats.MinBalance = balance;

            bool hasFirstStake = false;

            // Счётчики серий по сценариям.
            int curS1 = 0, curS2 = 0, curS3 = 0, curS4 = 0, curHedgeSl = 0;

            var nyTz = TimeZones.NewYork;

            // ===== Оптимизация по производительности =====
            int candleCount = candles1m.Count;
            if (candleCount == 0)
                return stats;

            var dayRanges = new (int StartIndex, int EndIndex)[days];

            int idxEntry = 0;
            int idxExit = 0;

            for (int i = 0; i < days; i++)
            {
                var row = mornings[i];

                // EntryUtc — доменный тип; для минуток нужен DateTime.
                DateTime entryTimeUtc = row.EntryUtc.Value;

                // baseline-exit по контракту NY-окон (единственный источник истины).
                DateTime exitUtc = NyWindowing.ComputeBaselineExitUtc(row.EntryUtc, nyTz).Value;

                while (idxEntry < candleCount && candles1m[idxEntry].OpenTimeUtc < entryTimeUtc)
                    idxEntry++;

                while (idxExit < candleCount && candles1m[idxExit].OpenTimeUtc < exitUtc)
                    idxExit++;

                dayRanges[i] = (idxEntry, idxExit);
            }

            // ===== Основной цикл по дням =====
            for (int i = 0; i < days; i++)
            {
                if (balance <= 0.0)
                    break;

                var row = mornings[i];
                var rec = records[i];

                if (!TryResolveDirection(rec.PredLabel, out int dirSign, out int labelClass))
                    continue;

                // ===== Anti-direction overlay по ответу SL-модели =====
                bool antiApplied = false;

                if (rec.SlHighDecision == true && dirSign > 0)
                {
                    dirSign = -1;
                    antiApplied = true;
                }

                rec.AntiDirectionApplied = antiApplied;

                bool isLongBase = dirSign > 0;

                var (startIndex, endIndex) = dayRanges[i];

                if (startIndex >= candleCount)
                    break;

                if (startIndex >= endIndex)
                    continue;

                var entryCandle = candles1m[startIndex];

                double entryPrice = entryCandle.Open;
                if (entryPrice <= 0.0)
                    continue;

                double totalStake = balance * parameters.TotalRiskFractionPerTrade;
                if (totalStake <= 0.0)
                    continue;

                if (!hasFirstStake)
                {
                    stats.StartTotalStake = totalStake;
                    stats.MinTotalStake = totalStake;
                    hasFirstStake = true;
                }
                else
                {
                    if (totalStake < stats.MinTotalStake)
                        stats.MinTotalStake = totalStake;
                }

                double baseStake = totalStake * baseStakeFraction;
                double hedgeStake = totalStake * hedgeStakeFraction;

                double qtyBase = baseStake / entryPrice;

                bool baseOpen = true;
                bool hedgeOpen = false;
                bool secondOpen = false;

                bool isLongHedge = !isLongBase;
                bool isLongSecond = isLongBase;

                double hedgeEntryPrice = 0.0, hedgeQty = 0.0;
                double secondEntryPrice = 0.0, secondQty = 0.0;

                double dayPnl = 0.0;
                int scenario = 0;
                bool closedByScenario = false;

                double lastPrice = entryCandle.Close;

                double baseTpPrice = entryPrice + dirSign * parameters.BaseTpOffsetUsd;
                double hedgeTriggerPrice = entryPrice - dirSign * parameters.HedgeTriggerOffsetUsd;

                for (int ci = startIndex; ci < endIndex; ci++)
                {
                    var c = candles1m[ci];
                    lastPrice = c.Close;

                    if (!hedgeOpen && !secondOpen)
                    {
                        if (HitTakeProfit(isLongBase, baseTpPrice, c))
                        {
                            double pnlBase = ComputePnl(isLongBase, qtyBase, entryPrice, baseTpPrice);
                            dayPnl = pnlBase;
                            scenario = 1;
                            closedByScenario = true;
                            baseOpen = false;
                            break;
                        }

                        if (!hedgeOpen)
                        {
                            if (HitHedgeTrigger(dirSign, hedgeTriggerPrice, c))
                            {
                                hedgeOpen = true;
                                hedgeEntryPrice = hedgeTriggerPrice;
                                isLongHedge = !isLongBase;
                                hedgeQty = hedgeStake / hedgeEntryPrice;
                            }
                        }
                    }

                    if (hedgeOpen && !secondOpen)
                    {
                        double hedgeTpPrice = hedgeEntryPrice + (-dirSign) * parameters.HedgeTpOffsetUsd;
                        double hedgeSlPrice = entryPrice;

                        if (HitTakeProfit(isLongHedge, hedgeTpPrice, c))
                        {
                            double pnlHedge = ComputePnl(isLongHedge, hedgeQty, hedgeEntryPrice, hedgeTpPrice);
                            double pnlBase = ComputePnl(isLongBase, qtyBase, entryPrice, hedgeTpPrice);

                            dayPnl = pnlBase + pnlHedge;
                            scenario = 2;
                            closedByScenario = true;

                            baseOpen = false;
                            hedgeOpen = false;
                            break;
                        }

                        if (HitStopLoss(isLongHedge, hedgeSlPrice, c))
                        {
                            double pnlHedge = ComputePnl(isLongHedge, hedgeQty, hedgeEntryPrice, hedgeSlPrice);
                            dayPnl += pnlHedge;

                            hedgeOpen = false;

                            secondOpen = true;
                            isLongSecond = isLongBase;
                            secondEntryPrice = hedgeSlPrice;
                            secondQty = hedgeStake / secondEntryPrice;

                            if (scenario == 0)
                                scenario = 3;

                            continue;
                        }
                    }

                    if (secondOpen)
                    {
                        double secondStopPrice = secondEntryPrice - dirSign * parameters.SecondLegStopOffsetUsd;

                        if (HitStopLoss(isLongSecond, secondStopPrice, c))
                        {
                            double pnlBase = ComputePnl(isLongBase, qtyBase, entryPrice, secondStopPrice);
                            double pnlSecond = ComputePnl(isLongSecond, secondQty, secondEntryPrice, secondStopPrice);

                            dayPnl += pnlBase + pnlSecond;
                            scenario = 4;
                            closedByScenario = true;

                            baseOpen = false;
                            secondOpen = false;
                            break;
                        }

                        double doubleTpPrice = entryPrice + dirSign * parameters.DoublePositionTpOffsetUsd;

                        if (HitTakeProfit(isLongSecond, doubleTpPrice, c))
                        {
                            double pnlBase = ComputePnl(isLongBase, qtyBase, entryPrice, doubleTpPrice);
                            double pnlSecond = ComputePnl(isLongSecond, secondQty, secondEntryPrice, doubleTpPrice);

                            dayPnl += pnlBase + pnlSecond;

                            if (scenario == 0)
                                scenario = 3;

                            closedByScenario = true;
                            baseOpen = false;
                            secondOpen = false;
                            break;
                        }
                    }
                }

                if (!closedByScenario)
                {
                    if (baseOpen)
                        dayPnl += ComputePnl(isLongBase, qtyBase, entryPrice, lastPrice);

                    if (hedgeOpen)
                        dayPnl += ComputePnl(isLongHedge, hedgeQty, hedgeEntryPrice, lastPrice);

                    if (secondOpen)
                        dayPnl += ComputePnl(isLongSecond, secondQty, secondEntryPrice, lastPrice);
                }

                stats.TradesCount++;
                stats.TotalPnlNet += dayPnl;

                if (dayPnl > 0)
                {
                    stats.ProfitTradesCount++;
                    stats.TotalProfitGross += dayPnl;
                    stats.TotalWithdrawnProfit += dayPnl;
                }
                else if (dayPnl < 0)
                {
                    stats.LossTradesCount++;
                    stats.TotalLossGross += dayPnl;
                    balance += dayPnl;
                    if (balance < 0) balance = 0;
                }

                stats.EndBalance = balance;
                if (balance < stats.MinBalance)
                {
                    stats.MinBalance = balance;
                    stats.MaxDrawdownAbs = stats.StartBalance - stats.MinBalance;
                    stats.MaxDrawdownPct = stats.StartBalance > 0
                        ? stats.MaxDrawdownAbs / stats.StartBalance
                        : 0.0;
                }

                switch (scenario)
                {
                    case 1:
                        stats.Scenario1Count++;
                        stats.Scenario1Pnl += dayPnl;
                        curS1++;
                        curS2 = curS3 = curS4 = 0;
                        break;

                    case 2:
                        stats.Scenario2Count++;
                        stats.Scenario2Pnl += dayPnl;
                        curS2++;
                        curS1 = curS3 = curS4 = 0;
                        break;

                    case 3:
                        stats.Scenario3Count++;
                        stats.Scenario3Pnl += dayPnl;
                        curS3++;
                        curS1 = curS2 = curS4 = 0;
                        break;

                    case 4:
                        stats.Scenario4Count++;
                        stats.Scenario4Pnl += dayPnl;
                        curS4++;
                        curS1 = curS2 = curS3 = 0;
                        break;

                    default:
                        curS1 = curS2 = curS3 = curS4 = 0;
                        break;
                }

                if (scenario == 3 || scenario == 4)
                    curHedgeSl++;
                else
                    curHedgeSl = 0;

                if (curS1 > stats.MaxScenario1Streak) stats.MaxScenario1Streak = curS1;
                if (curS2 > stats.MaxScenario2Streak) stats.MaxScenario2Streak = curS2;
                if (curS3 > stats.MaxScenario3Streak) stats.MaxScenario3Streak = curS3;
                if (curS4 > stats.MaxScenario4Streak) stats.MaxScenario4Streak = curS4;
                if (curHedgeSl > stats.MaxHedgeSlStreak) stats.MaxHedgeSlStreak = curHedgeSl;

                switch (labelClass)
                {
                    case 2:
                        stats.TotalPredUpCount++;
                        stats.TotalPredUpPnl += dayPnl;
                        break;

                    case 0:
                        stats.TotalPredDownCount++;
                        stats.TotalPredDownPnl += dayPnl;
                        break;

                    case 1:
                        stats.TotalPredFlatCount++;
                        stats.TotalPredFlatPnl += dayPnl;
                        break;
                }
            }

            return stats;
        }

        private static bool TryResolveDirection(int predLabel, out int dirSign, out int labelClass)
        {
            labelClass = predLabel;

            switch (predLabel)
            {
                case 2:
                    dirSign = +1;
                    return true;

                case 0:
                    dirSign = -1;
                    return true;

                case 1:
                    dirSign = +1;
                    return true;

                default:
                    dirSign = 0;
                    return false;
            }
        }

        private static double ComputePnl(bool isLong, double qty, double entryPrice, double exitPrice)
        {
            if (qty <= 0.0) return 0.0;
            double delta = exitPrice - entryPrice;
            return isLong ? qty * delta : qty * (-delta);
        }

        private static bool HitTakeProfit(bool isLong, double tpPrice, Candle1m c)
        {
            return isLong ? c.High >= tpPrice : c.Low <= tpPrice;
        }

        private static bool HitStopLoss(bool isLong, double slPrice, Candle1m c)
        {
            return isLong ? c.Low <= slPrice : c.High >= slPrice;
        }

        private static bool HitHedgeTrigger(int dirSign, double triggerPrice, Candle1m c)
        {
            return dirSign > 0 ? c.Low <= triggerPrice : c.High >= triggerPrice;
        }
    }
}
