using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
	{
	/// <summary>
	/// Итоговый результат multi-round RSI-стратегии.
	/// Содержит:
	/// - агрегаты по капиталу и риску;
	/// - глобальные трейдовые метрики;
	/// - дневную статистику и equity-curve;
	/// - распределения по дням недели, часам входа, ATR-квантилям;
	/// - хвостовые дни (5 % лучших/худших) и серийность по дням.
	/// </summary>
	public sealed class MultiRoundStrategyResult
		{
		// Капитал и риск
		public double StartBalanceUsd { get; set; }
		public double EndBalanceUsd { get; set; }
		public double WithdrawnProfitUsd { get; set; }

		public double MaxDrawdownUsd { get; set; }
		public double MaxDrawdownPct { get; set; }

		public double StakeStartUsd { get; set; }
		public double StakeMinUsd { get; set; }
		public double StakeMinDrawdownPct { get; set; }

		// Трейды
		public int TradesTotal { get; set; }
		public int TradesProfitable { get; set; }
		public int TradesLossy { get; set; }

		/// <summary>Сумма прибыльных трейдов (>= 0).</summary>
		public double GrossProfitUsd { get; set; }

		/// <summary>Сумма убыточных трейдов (<= 0).</summary>
		public double GrossLossUsd { get; set; }

		// По дням
		public int DaysTotal { get; set; }
		public double AvgTradesPerDay { get; set; }
		public int MaxTradesInSingleDay { get; set; }

		// Типы выхода
		public int ExitTpCount { get; set; }
		public int ExitSlCount { get; set; }
		public int ExitTimeCount { get; set; }

		/// <summary>Максимальная серия убыточных дней подряд.</summary>
		public int MaxLosingStreakDays { get; set; }

		/// <summary>Детализированная статистика по каждому дню.</summary>
		public List<StrategyDayStats> DayStats { get; } = new ();

		/// <summary>Equity-curve по дням (balance + withdrawn).</summary>
		public List<EquityPoint> EquityCurve { get; } = new ();

		/// <summary>Распределение PnL по дням недели.</summary>
		public Dictionary<DayOfWeek, WeekdayBucketStats> PnlByWeekday { get; } = new ();

		/// <summary>Распределение PnL по часу входа (локальное время NY).</summary>
		public Dictionary<int, EntryHourBucketStats> PnlByEntryHourLocal { get; } = new ();

		/// <summary>Распределение PnL по ATR-квантилям.</summary>
		public List<VolatilityBucketStats> PnlByAtrBucket { get; } = new ();

		/// <summary>Худшие дни (примерно 5 % по PnL).</summary>
		public List<StrategyDayStats> WorstDays { get; } = new ();

		/// <summary>Лучшие дни (примерно 5 % по PnL).</summary>
		public List<StrategyDayStats> BestDays { get; } = new ();
		}

	/// <summary>Статистика по одному календарному дню.</summary>
	public sealed class StrategyDayStats
		{
		public DateTime DateUtc { get; set; }
		public double DayPnlUsd { get; set; }
		public int Trades { get; set; }
		public double AtrPct { get; set; }
		}

	/// <summary>Точка equity-curve по дню.</summary>
	public sealed class EquityPoint
		{
		public DateTime DateUtc { get; set; }
		public double EquityUsd { get; set; }
		}

	/// <summary>Агрегаты PnL по дню недели.</summary>
	public sealed class WeekdayBucketStats
		{
		public DayOfWeek DayOfWeek { get; set; }
		public int Days { get; set; }
		public int Trades { get; set; }
		public double PnlUsd { get; set; }
		}

	/// <summary>Агрегаты PnL по часу входа (локальное время NY).</summary>
	public sealed class EntryHourBucketStats
		{
		public int HourLocal { get; set; }
		public int Trades { get; set; }
		public double PnlUsd { get; set; }
		}

	/// <summary>Агрегаты PnL по ATR-квантилю.</summary>
	public sealed class VolatilityBucketStats
		{
		public string Name { get; set; } = string.Empty;
		public double AtrFrom { get; set; }
		public double AtrTo { get; set; }
		public int Days { get; set; }
		public int Trades { get; set; }
		public double PnlUsd { get; set; }
		}
	}
