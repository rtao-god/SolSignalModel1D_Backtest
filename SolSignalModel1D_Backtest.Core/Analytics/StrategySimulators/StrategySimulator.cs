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
	/// Агрегированная статистика по стратегии:
	/// - PnL по сценариям (1..4);
	/// - общие метрики по дням;
	/// - разрез по PredLabel;
	/// - серии по сценариям и по дням, где срабатывает SL хеджа.
	/// </summary>
	public sealed class StrategyStats
		{
		// --- Капитал и объёмы ---

		/// <summary>Стартовый баланс кошелька (в начале прогона).</summary>
		public double StartBalance { get; set; }

		/// <summary>Финальный баланс после всех сделок (учёт только убытков, профит выводится).</summary>
		public double EndBalance { get; set; }

		/// <summary>Минимальный баланс в процессе (для расчёта просадки).</summary>
		public double MinBalance { get; set; }

		/// <summary>Максимальная просадка в абсолюте (StartBalance - MinBalance).</summary>
		public double MaxDrawdownAbs { get; set; }

		/// <summary>Максимальная просадка в долях от стартового баланса.</summary>
		public double MaxDrawdownPct { get; set; }

		/// <summary>Общий риск на сделку в день 1 (в долларах).</summary>
		public double StartTotalStake { get; set; }

		/// <summary>Минимальный общий риск на сделку (в долларах) по мере падения капитала.</summary>
		public double MinTotalStake { get; set; }

		/// <summary>Сколько денег всего было выведено с профитных дней.</summary>
		public double TotalWithdrawnProfit { get; set; }

		// --- Общие метрики по дням ---

		/// <summary>Всего "дней-сделок", по которым удалось что-то посчитать.</summary>
		public int TradesCount { get; set; }

		/// <summary>Количество прибыльных дней (PnL &gt; 0).</summary>
		public int ProfitTradesCount { get; set; }

		/// <summary>Количество убыточных дней (PnL &lt; 0).</summary>
		public int LossTradesCount { get; set; }

		/// <summary>Суммарный чистый PnL по всем дням (profit + loss).</summary>
		public double TotalPnlNet { get; set; }

		/// <summary>Суммарный валовый профит (только плюс).</summary>
		public double TotalProfitGross { get; set; }

		/// <summary>Суммарный валовый убыток (отрицательное число).</summary>
		public double TotalLossGross { get; set; }

		// --- Сценарии (как в описании стратегии) ---

		public int Scenario1Count { get; set; }
		public double Scenario1Pnl { get; set; }

		public int Scenario2Count { get; set; }
		public double Scenario2Pnl { get; set; }

		public int Scenario3Count { get; set; }
		public double Scenario3Pnl { get; set; }

		public int Scenario4Count { get; set; }
		public double Scenario4Pnl { get; set; }

		// --- Серии по сценариям ---

		public int MaxScenario1Streak { get; set; }
		public int MaxScenario2Streak { get; set; }
		public int MaxScenario3Streak { get; set; }
		public int MaxScenario4Streak { get; set; }

		/// <summary>Максимальная серия дней, когда стоп шорта срабатывал (сценарии 3 или 4).</summary>
		public int MaxHedgeSlStreak { get; set; }

		// --- Разрез по PredLabel ---

		/// <summary>PredLabel = 2 (up).</summary>
		public int TotalPredUpCount { get; set; }
		public double TotalPredUpPnl { get; set; }

		/// <summary>PredLabel = 0 (down).</summary>
		public int TotalPredDownCount { get; set; }
		public double TotalPredDownPnl { get; set; }

		/// <summary>PredLabel = 1 (flat).</summary>
		public int TotalPredFlatCount { get; set; }
		public double TotalPredFlatPnl { get; set; }
		}

	/// <summary>
	/// Симуляция стратегии на основе прогнозов модели и минутных свечей.
	/// Важно:
	/// - стратегия не использует PnL-движок, только PredictionRecord + 1m;
	/// - стейк берётся как доля текущего капитала;
	/// - профитные дни выводятся (капитал не растёт);
	/// - убытки уменьшают капитал → последующие сделки идут меньшим объёмом.
	/// </summary>
	public static class StrategySimulator
		{
		// Параметры стратегии (в долларах).
		// Эти числа задают "форму" сценариев, а не абсолютный риск:
		// реальные объёмы считаются как доля капитала и масштабируются.
		private const double BaseStakeUsd = 1200.0;    // первая нога (первый лонг/шорт)
		private const double HedgeStakeUsd = 2200.0;   // вторая нога (шорт/лонг-hedge и второй вход)

		// Смещения по цене (в долларах от точки входа).
		private const double BaseTpOffsetUsd = 3.0;         // TP базовой позиции
		private const double HedgeTriggerOffsetUsd = 2.0;   // триггер открытия хеджа
		private const double HedgeStopOffsetUsd = 1.0;      // SL хеджа
		private const double HedgeTpOffsetUsd = 5.0;        // TP хеджа
		private const double SecondLegStopOffsetUsd = 0.5;  // SL для "двойного" входа

		// Параметры risk-management.
		private const double InitialBalanceUsd = 10_000.0;       // стартовый баланс кошелька
		private const double TotalRiskFractionPerTrade = 0.30;   // доля капитала на сделку (30%)

		private static readonly double BaseStakeFraction =
			BaseStakeUsd / (BaseStakeUsd + HedgeStakeUsd);

		private static readonly double HedgeStakeFraction =
			HedgeStakeUsd / (BaseStakeUsd + HedgeStakeUsd);

		/// <summary>
		/// Запуск симуляции стратегии на сигнальных днях.
		/// mornings и records используются по минимальной длине, если длины не совпадают.
		/// </summary>
		public static StrategyStats Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));

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

			// Текущий баланс и базовые метрики по капиталу.
			double balance = InitialBalanceUsd;
			stats.StartBalance = balance;
			stats.MinBalance = balance;

			bool hasFirstStake = false;

			// Счётчики серий по сценариям.
			int curS1 = 0, curS2 = 0, curS3 = 0, curS4 = 0, curHedgeSl = 0;

			var nyTz = TimeZones.NewYork;

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

				bool isLongBase = dirSign > 0;   // Pred up/flat → базовый лонг; Pred down → базовый шорт.

				// Время входа — дата дневной строки (NY-утро).
				DateTime entryTime = row.Date;

				// Находим первую минутную свечу не раньше entryTime.
				var entryCandle = candles1m.FirstOrDefault (c => c.OpenTimeUtc >= entryTime);
				if (entryCandle == null)
					{
					// Нет минутных данных — этот день пропускаем.
					continue;
					}

				double entryPrice = entryCandle.Open;
				if (entryPrice <= 0.0)
					continue;

				// Рассчитываем время выхода: следующее NY-утро 08:00 - 2 минуты.
				var entryLocal = TimeZoneInfo.ConvertTimeFromUtc (entryTime, nyTz);
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

				DateTime exitUtc = TimeZoneInfo.ConvertTimeToUtc (exitLocal, nyTz)
					.AddMinutes (-2);

				// Расчёт общего объёма риска на сделку и разбиение на две ноги.
				double totalStake = balance * TotalRiskFractionPerTrade;
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

				double baseStake = totalStake * BaseStakeFraction;
				double hedgeStake = totalStake * HedgeStakeFraction;

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
				double baseTpPrice = entryPrice + dirSign * BaseTpOffsetUsd;
				double hedgeTriggerPrice = entryPrice - dirSign * HedgeTriggerOffsetUsd;

				foreach (var c in candles1m)
					{
					if (c.OpenTimeUtc < entryTime || c.OpenTimeUtc >= exitUtc)
						continue;

					lastPrice = c.Close;

					// 1) Сценарий 1: базовый TP, если ещё не открывали хедж или вторую ногу.
					if (!hedgeOpen && !secondOpen)
						{
						if (HitTakeProfit (isLongBase, baseTpPrice, c))
							{
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
							hedgeEntryPrice + (-dirSign) * HedgeTpOffsetUsd;
						double hedgeSlPrice =
							hedgeEntryPrice - (-dirSign) * HedgeStopOffsetUsd;

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
						// открываем вторую ногу в направлении базовой позиции.
						if (HitStopLoss (isLongHedge, hedgeSlPrice, c))
							{
							double pnlHedge = ComputePnl (isLongHedge, hedgeQty, hedgeEntryPrice, hedgeSlPrice);
							dayPnl += pnlHedge; // как правило, отрицательный.

							hedgeOpen = false;

							secondOpen = true;
							isLongSecond = isLongBase;
							secondEntryPrice = hedgeSlPrice;
							secondQty = hedgeStake / secondEntryPrice;

							// Минимум сценарий 3 (SL хеджа). Если потом сработает стоп двойного входа — станет сценарий 4.
							if (scenario == 0)
								scenario = 3;

							continue;
							}
						}

					// 4) Вторая нога открыта — проверяем слабый откат против направления (сценарий 4).
					if (secondOpen)
						{
						double secondStopPrice =
							secondEntryPrice - dirSign * SecondLegStopOffsetUsd;

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

				case 1: // flat → пока трактуем как лонг
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
		}
	}
