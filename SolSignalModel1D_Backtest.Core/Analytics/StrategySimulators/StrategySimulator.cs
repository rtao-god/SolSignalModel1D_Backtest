using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

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
	///   3) хедж выбило по SL, открыли вторую ногу по тренду, и рынок ушёл в нашу сторону (в т.ч. новый TP=5$);
	///   4) хедж выбило, открыли вторую ногу и нас выбило по слабому откату против тренда.
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
		public static StrategyStats Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			StrategyParameters parameters )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));
			if (parameters == null) throw new ArgumentNullException (nameof (parameters));

			var stats = new StrategyStats ();

			if (mornings.Count == 0 || records.Count == 0)
				return stats;

			int days = Math.Min (mornings.Count, records.Count);
			if (mornings.Count != records.Count)
				{
				Console.WriteLine (
					$"[strategy] WARN: mornings={mornings.Count}, records={records.Count}, " +
					$"использую первые {days} точек для симуляции.");
				}

			// Предрасчитываем доли стейка между базовой ногой и хеджем,
			// чтобы не повторять одну и ту же математику в каждом дне.
			double totalStakeShape = parameters.BaseStakeUsd + parameters.HedgeStakeUsd;
			if (totalStakeShape <= 0.0)
				throw new InvalidOperationException ("[strategy] BaseStakeUsd + HedgeStakeUsd must be > 0.");

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
			// Вместо того, чтобы для каждого дня перебирать все 1m-свечи,
			// заранее считаем для каждого дня диапазон индексов [start; end)
			// по candles1m, попадающий в окно [entry; exit).
			//
			// Предпосылки:
			// - mornings идут по датам по возрастанию;
			// - candles1m отсортированы по OpenTimeUtc по возрастанию.
			//
			// В результате каждая 1m-свеча участвует максимум в одном дне,
			// и общий цикл становится O(days + candles1m) вместо O(days * candles1m).
			int candleCount = candles1m.Count;
			if (candleCount == 0)
				return stats;

			var dayRanges = new (int StartIndex, int EndIndex)[days];

			int idxEntry = 0;
			int idxExit = 0;

			for (int i = 0; i < days; i++)
				{
				var row = mornings[i];
				DateTime entryTimeUtc = row.Date;
				DateTime exitUtc = ComputeExitWindow (entryTimeUtc, nyTz);

				// Сдвигаем указатель входа до первой свечи с t >= entryTimeUtc.
				while (idxEntry < candleCount && candles1m[idxEntry].OpenTimeUtc < entryTimeUtc)
					{
					idxEntry++;
					}

				// Сдвигаем указатель выхода до первой свечи с t >= exitUtc.
				while (idxExit < candleCount && candles1m[idxExit].OpenTimeUtc < exitUtc)
					{
					idxExit++;
					}

				dayRanges[i] = (idxEntry, idxExit);
				}

			// ===== Основной цикл по дням =====
			for (int i = 0; i < days; i++)
				{
				if (balance <= 0.0)
					{
					// Баланс обнулён — дальше симуляция не имеет смысла.
					break;
					}

				var row = mornings[i];
				var rec = records[i];

				// Определяем направление торговли по PredLabel.
				if (!TryResolveDirection (rec.PredLabel, out int dirSign, out int labelClass))
					{
					// Непонятная метка — пропускаем день.
					continue;
					}

				// ===== Anti-direction overlay по ответу SL-модели =====
				// SlHighDecision == true трактуем как "рискованный день".
				// В таком дне лонги запрещаем: если базовый сигнал лонг (dirSign > 0),
				// переворачиваем направление в шорт и помечаем AntiDirectionApplied = true.
				// Если базовый сигнал шорт, ничего не меняем и флаг не ставим.
				bool antiApplied = false;

				if (rec.SlHighDecision && dirSign > 0)
					{
					dirSign = -1;
					antiApplied = true;
					}

				rec.AntiDirectionApplied = antiApplied;

				// Итоговое направление базовой ноги после возможного overlay.
				bool isLongBase = dirSign > 0;   // +1 = лонг, -1 = шорт

				// Время входа — дата дневной строки (NY-утро вью).
				DateTime entryTimeUtc = row.Date;

				// Индексы минуток для текущего дня.
				var (startIndex, endIndex) = dayRanges[i];

				// Если вообще нет минутных свечей после entryTime — дальше смысла нет:
				// следующих дней тоже не получится симулировать.
				if (startIndex >= candleCount)
					{
					break;
					}

				// Первая свеча дня — первая с t >= entryTimeUtc.
				var entryCandle = candles1m[startIndex];

				double entryPrice = entryCandle.Open;
				if (entryPrice <= 0.0)
					continue;

				// Окно торговли теперь задаётся индексами [startIndex; endIndex),
				// поэтому повторно ComputeExitWindow не вызываем и по времени
				// выходим из цикла через границу endIndex.

				// Расчёт общего объёма риска на сделку и разбиение на две ноги.
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

				// Состояния позиций за день.
				bool baseOpen = true;
				bool hedgeOpen = false;
				bool secondOpen = false;

				bool isLongHedge = !isLongBase;
				bool isLongSecond = isLongBase;

				double hedgeEntryPrice = 0.0, hedgeQty = 0.0;
				double secondEntryPrice = 0.0, secondQty = 0.0;

				// PnL за день.
				double dayPnl = 0.0;
				int scenario = 0;             // 0 = "без спец-сценария", 1..4 = сценарии.
				bool closedByScenario = false;

				double lastPrice = entryCandle.Close;

				// Предрасчитанные уровни для базовой позиции.
				double baseTpPrice = entryPrice + dirSign * parameters.BaseTpOffsetUsd;
				double hedgeTriggerPrice = entryPrice - dirSign * parameters.HedgeTriggerOffsetUsd;

				// Цикл только по минуткам текущего дня: [startIndex; endIndex).
				for (int ci = startIndex; ci < endIndex; ci++)
					{
					var c = candles1m[ci];

					lastPrice = c.Close;

					// 1) Сценарий 1: базовый TP, если ещё не открывали хедж или вторую ногу.
					if (!hedgeOpen && !secondOpen)
						{
						if (HitTakeProfit (isLongBase, baseTpPrice, c))
							{
							// Классический сценарий 1: один лонг/шорт до TP=+3$.
							double pnlBase = ComputePnl (isLongBase, qtyBase, entryPrice, baseTpPrice);
							dayPnl = pnlBase;
							scenario = 1;
							closedByScenario = true;
							baseOpen = false;
							break;
							}

						// 2) Открытие хеджа при достижении триггера.
						if (!hedgeOpen)
							{
							if (HitHedgeTrigger (dirSign, hedgeTriggerPrice, c))
								{
								hedgeOpen = true;
								hedgeEntryPrice = hedgeTriggerPrice;
								isLongHedge = !isLongBase;
								hedgeQty = hedgeStake / hedgeEntryPrice;
								}
							}
						}

					// 3) Хедж открыт, но вторая нога ещё нет — проверяем TP/SL хеджа.
					if (hedgeOpen && !secondOpen)
						{
						double hedgeTpPrice =
							hedgeEntryPrice + (-dirSign) * parameters.HedgeTpOffsetUsd;

						// SL хеджа и точка входа второй ноги совпадают с исходной ценой входа дня.
						// Это даёт паттерн 200 → 198 → 200:
						//   - на 198 открыли шорт-хедж;
						//   - на 200 закрыли хедж и открыли второй лонг.
						double hedgeSlPrice = entryPrice;

						// 3.1) TP хеджа (сценарий 2): закрываем базу и хедж по цене TP хеджа.
						if (HitTakeProfit (isLongHedge, hedgeTpPrice, c))
							{
							double pnlHedge = ComputePnl (isLongHedge, hedgeQty, hedgeEntryPrice, hedgeTpPrice);
							double pnlBase = ComputePnl (isLongBase, qtyBase, entryPrice, hedgeTpPrice);

							dayPnl = pnlBase + pnlHedge;
							scenario = 2;
							closedByScenario = true;

							baseOpen = false;
							hedgeOpen = false;
							break;
							}

						// 3.2) SL хеджа (сценарий 3): фиксируем убыток по хеджу,
						// открываем вторую ногу в направлении базовой позиции по той же цене (entryPrice).
						if (HitStopLoss (isLongHedge, hedgeSlPrice, c))
							{
							double pnlHedge = ComputePnl (isLongHedge, hedgeQty, hedgeEntryPrice, hedgeSlPrice);
							dayPnl += pnlHedge; // обычно отрицательный PnL

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

					// 4) Вторая нога открыта — сначала проверяем SL, затем TP двойной позиции.
					if (secondOpen)
						{
						// SL второй ноги: «шум» против тренда, сценарий 4.
						double secondStopPrice =
							secondEntryPrice - dirSign * parameters.SecondLegStopOffsetUsd;

						if (HitStopLoss (isLongSecond, secondStopPrice, c))
							{
							double pnlBase = ComputePnl (isLongBase, qtyBase, entryPrice, secondStopPrice);
							double pnlSecond = ComputePnl (isLongSecond, secondQty, secondEntryPrice, secondStopPrice);

							dayPnl += pnlBase + pnlSecond;
							scenario = 4;
							closedByScenario = true;

							baseOpen = false;
							secondOpen = false;
							break;
							}

						// TP двойной позиции: рынок переобулся и прошёл дальше.
						// ОБА лонга/шорта закрываем по более далёкой цели (+5$ от первой точки).
						double doubleTpPrice = entryPrice + dirSign * parameters.DoublePositionTpOffsetUsd;

						if (HitTakeProfit (isLongSecond, doubleTpPrice, c))
							{
							double pnlBase = ComputePnl (isLongBase, qtyBase, entryPrice, doubleTpPrice);
							double pnlSecond = ComputePnl (isLongSecond, secondQty, secondEntryPrice, doubleTpPrice);

							dayPnl += pnlBase + pnlSecond;

							// Логически это развитие сценария 3 (хедж выбило, потом рынок пошёл как надо).
							if (scenario == 0)
								scenario = 3;

							closedByScenario = true;
							baseOpen = false;
							secondOpen = false;
							break;
							}
						}
					}

				// Если ни один сценарий не сработал — закрываем все открытые позиции по цене последней минуты.
				if (!closedByScenario)
					{
					if (baseOpen)
						{
						double pnlBase = ComputePnl (isLongBase, qtyBase, entryPrice, lastPrice);
						dayPnl += pnlBase;
						}

					if (hedgeOpen)
						{
						double pnlHedge = ComputePnl (isLongHedge, hedgeQty, hedgeEntryPrice, lastPrice);
						dayPnl += pnlHedge;
						}

					if (secondOpen)
						{
						double pnlSecond = ComputePnl (isLongSecond, secondQty, secondEntryPrice, lastPrice);
						dayPnl += pnlSecond;
						}
					}

				// --- Обновляем капитал и агрегаты по PnL ---

				stats.TradesCount++;
				stats.TotalPnlNet += dayPnl;

				if (dayPnl > 0)
					{
					stats.ProfitTradesCount++;
					stats.TotalProfitGross += dayPnl;
					stats.TotalWithdrawnProfit += dayPnl;
					// Профит выводим → баланс не растёт.
					}
				else if (dayPnl < 0)
					{
					stats.LossTradesCount++;
					stats.TotalLossGross += dayPnl;
					balance += dayPnl; // dayPnl < 0 → баланс уменьшается
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

				// --- Обновляем статистику по сценариям и сериям ---

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

				// --- Обновляем метрики по PredLabel ---

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

		/// <summary>
		/// Разбор PredLabel: куда торговать и к какому классу отнести PnL.
		/// dirSign: +1 = базовый лонг, -1 = базовый шорт.
		/// labelClass: оригинальное значение PredLabel (0/1/2).
		/// </summary>
		private static bool TryResolveDirection ( int predLabel, out int dirSign, out int labelClass )
			{
			labelClass = predLabel;

			switch (predLabel)
				{
				case 2: // up
					dirSign = +1;
					return true;

				case 0: // down
					dirSign = -1;
					return true;

				case 1: // flat → трактуем как лонг
					dirSign = +1;
					return true;

				default:
					dirSign = 0;
					return false;
				}
			}

		/// <summary>Расчёт PnL по позиции (лонг/шорт).</summary>
		private static double ComputePnl ( bool isLong, double qty, double entryPrice, double exitPrice )
			{
			if (qty <= 0.0) return 0.0;
			double delta = exitPrice - entryPrice;
			return isLong ? qty * delta : qty * (-delta);
			}

		/// <summary>Проверка достижения TP для позиции (лонг/шорт).</summary>
		private static bool HitTakeProfit ( bool isLong, double tpPrice, Candle1m c )
			{
			return isLong
				? c.High >= tpPrice
				: c.Low <= tpPrice;
			}

		/// <summary>Проверка достижения SL для позиции (лонг/шорт).</summary>
		private static bool HitStopLoss ( bool isLong, double slPrice, Candle1m c )
			{
			return isLong
				? c.Low <= slPrice
				: c.High >= slPrice;
			}

		/// <summary>
		/// Проверка срабатывания триггера хеджа:
		/// - для базового лонга — цена должна сходить вниз;
		/// - для базового шорта — цена должна сходить вверх.
		/// dirSign: +1 = базовый лонг, -1 = базовый шорт.
		/// </summary>
		private static bool HitHedgeTrigger ( int dirSign, double triggerPrice, Candle1m c )
			{
			return dirSign > 0
				? c.Low <= triggerPrice
				: c.High >= triggerPrice;
			}

		/// <summary>
		/// Вычисляет время выхода: следующее NY-утро 08:00 (минус 2 минуты).
		/// Логика совпадает с тем, что уже используется в backtest-окнах.
		/// </summary>
		private static DateTime ComputeExitWindow ( DateTime entryTimeUtc, TimeZoneInfo nyTz )
			{
			var entryLocal = TimeZoneInfo.ConvertTimeFromUtc (entryTimeUtc, nyTz);
			var exitDateLocal = entryLocal.Date.AddDays (1);

			if (exitDateLocal.DayOfWeek == DayOfWeek.Saturday)
				exitDateLocal = exitDateLocal.AddDays (2);
			else if (exitDateLocal.DayOfWeek == DayOfWeek.Sunday)
				exitDateLocal = exitDateLocal.AddDays (1);

			var exitLocal = new DateTime (
				exitDateLocal.Year,
				exitDateLocal.Month,
				exitDateLocal.Day,
				8, 0, 0);

			return TimeZoneInfo.ConvertTimeToUtc (exitLocal, nyTz)
				.AddMinutes (-2);
			}
		}
	}
