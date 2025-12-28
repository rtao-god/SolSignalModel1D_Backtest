using System;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	/// <summary>
	/// Одна зафиксированная торговая операция в PnL.
	/// </summary>
	public sealed class PnLTrade
		{
		/// <summary>
		/// Дата сигнала / торгового дня (UTC, начало окна).
		/// </summary>
		public DateTime DateUtc { get; set; }

		/// <summary>
		/// Фактическое время входа в сделку (UTC).
		/// Для daily = утро NY, для delayed = момент исполнения отложенного входа.
		/// </summary>
		public DateTime EntryTimeUtc { get; set; }

		/// <summary>
		/// Фактическое время выхода из сделки (UTC).
		/// Это может быть:
		/// - момент срабатывания TP/SL,
		/// - baseline-выход (следующее NY-утро - 2 минуты),
		/// - момент ликвидации (если цена дошла до LiqPriceBacktest).
		/// </summary>
		public DateTime ExitTimeUtc { get; set; }

		/// <summary>
		/// Направление позиции: true = long, false = short.
		/// </summary>
		public bool IsLong { get; set; }

		/// <summary>
		/// Цена входа (SOL).
		/// </summary>
		public double EntryPrice { get; set; }

		/// <summary>
		/// Цена выхода (SOL) с учётом возможной принудительной ликвидации.
		/// Если по пути была ликвидация по backtest-цене (LiqPriceBacktest),
		/// то ExitPrice фиксируется именно на этом уровне.
		/// </summary>
		public double ExitPrice { get; set; }

		/// <summary>
		/// Сколько денег из корзины реально зашло в сделку (до плеча), в USDT.
		/// Исторически использовалось для агрегации по источникам.
		/// Сейчас семантически совпадает с MarginUsed, но оставлено для совместимости.
		/// </summary>
		public double PositionUsd { get; set; }

		/// <summary>
		/// Фактически задействованная маржа под позицию (USDT).
		/// marginUsed = min(целевой размер позиции в бакете, текущая equity бакета).
		/// Все PnL/комиссии считаются относительно этой величины.
		/// </summary>
		public double MarginUsed { get; set; }

		/// <summary>
		/// До комиссий, в % к цене (движение инструмента без плеча).
		/// Пример: 0.05 = +5% цены.
		/// </summary>
		public double GrossReturnPct { get; set; }

		/// <summary>
		/// После комиссий, в % к PositionUsd / MarginUsed.
		/// Т.е. реальная доходность по использованной марже.
		/// </summary>
		public double NetReturnPct { get; set; }

		/// <summary>
		/// Суммарная комиссия по сделке (вход + выход), в USDT.
		/// </summary>
		public double Commission { get; set; }

		/// <summary>
		/// Equity корзины после сделки (после учёта PnL, комиссий,
		/// ликвидации и возможного вывода сверх базового капитала).
		/// Для cross это equity соответствующего бакета, уже обрезанная до BaseCapital.
		/// </summary>
		public double EquityAfter { get; set; }

		/// <summary>
		/// Флаг, что сделка была закрыта ликвидацией по backtest-цене
		/// или что в процессе сделки бакет фактически обнулился/умер.
		/// Такой флаг удобен для подсчёта «жёстких» аварийных выходов на уровне модели.
		/// </summary>
		public bool IsLiquidated { get; set; }

		/// <summary>
		/// Флаг, что в бэктесте цена дошла до backtest-уровня ликвидации (LiqPriceBacktest),
		/// вычисленного на основе теоретической ликвидации LiqPrice, но немного смещённого
		/// ближе к цене входа.
		/// Этот флаг используется как прокси «реальной» биржевой ликвидации с учётом
		/// проскальзываний, комиссий и фандинга.
		/// </summary>
		public bool IsRealLiquidation { get; set; }

		/// <summary>
		/// Фактическое плечо, применённое к сделке (из политики плеча).
		/// </summary>
		public double LeverageUsed { get; set; }

		/// <summary>
		/// Теоретическая цена ликвидации по формуле:
		/// IMR = 1 / LeverageUsed, MMR = MaintenanceMarginRate.
		/// LiqAdversePct = IMR - MMR, далее LiqPrice = EntryPrice * (1 ± LiqAdversePct).
		/// Не зависит от конкретного пути цены; только от EntryPrice и LeverageUsed.
		/// Это опорная теоретическая ликва, близкая к биржевой.
		/// </summary>
		public double LiqPrice { get; set; }

		/// <summary>
		/// Backtest-цена ликвидации, по которой реально срабатывает ликва в моделировании.
		/// Рассчитывается на базе теоретической LiqPrice, но с немного меньшим
		/// расстоянием до точки входа, чтобы грубо учесть проскальзывание, комиссии
		/// и фандинг и получить более консервативный сценарий.
		/// Именно эта цена используется в PnL и для IsLiquidated/IsRealLiquidation.
		/// </summary>
		public double LiqPriceBacktest { get; set; }

		/// <summary>
		/// Источник сигнала: Daily / DelayedA / DelayedB.
		/// Используется для разреза метрик по слоям модели.
		/// </summary>
		public string Source { get; set; } = "";   // Daily / DelayedA / DelayedB

		/// <summary>
		/// Корзина капитала: daily / intraday / delayed.
		/// Нужна для агрегации по бакетам и расчёта их отдельных equity/withdrawals.
		/// </summary>
		public string Bucket { get; set; } = "";   // daily / intraday / delayed

		/// <summary>
		/// Max adverse excursion по 1m-пути, в долях от Entry (положительное число).
		/// Для лонга = max( (Entry - Low) / Entry ),
		/// для шорта = max( (High - Entry) / Entry ).
		/// Пример: 0.12 = -12% максимальной просадки по пути.
		/// </summary>
		public double MaxAdversePct { get; set; }

		/// <summary>
		/// Max favorable excursion по 1m-пути, в долях от Entry (положительное число).
		/// Для лонга = max( (High - Entry) / Entry ),
		/// для шорта = max( (Entry - Low) / Entry ).
		/// Пример: 0.18 = +18% максимального движения в нужную сторону.
		/// </summary>
		public double MaxFavorablePct { get; set; }
		}
	}
