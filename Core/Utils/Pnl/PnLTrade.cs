using System;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// Одна зафиксированная торговая операция в PnL.
	/// </summary>
	public sealed class PnLTrade
		{
		public DateTime DateUtc { get; set; }
		public DateTime EntryTimeUtc { get; set; }
		public DateTime ExitTimeUtc { get; set; }

		public bool IsLong { get; set; }

		/// <summary>Цена входа (SOL).</summary>
		public double EntryPrice { get; set; }

		/// <summary>Цена выхода (SOL) с учётом ликвидации.</summary>
		public double ExitPrice { get; set; }

		/// <summary>
		/// Сколько денег из корзины реально зашло в сделку (до плеча).
		/// Нужно для агрегации по источникам.
		/// </summary>
		public double PositionUsd { get; set; }

		/// <summary>До комиссий, в % к цене.</summary>
		public double GrossReturnPct { get; set; }

		/// <summary>После комиссий, в % к PositionUsd.</summary>
		public double NetReturnPct { get; set; }

		/// <summary>Комиссия в USDT.</summary>
		public double Commission { get; set; }

		/// <summary>Equity корзины после сделки (cross, обрезанная).</summary>
		public double EquityAfter { get; set; }

		public bool IsLiquidated { get; set; }

		/// <summary>Фактическое плечо.</summary>
		public double LeverageUsed { get; set; }

		/// <summary>Источник сигнала: Daily / DelayedA / DelayedB.</summary>
		public string Source { get; set; } = "";   // Daily / DelayedA / DelayedB

		/// <summary>Корзина: daily / intraday / delayed.</summary>
		public string Bucket { get; set; } = "";   // daily / intraday / delayed

		/// <summary>
		/// Max adverse excursion по 1m-пути, в процентах от Entry (положительное число).
		/// Для лонга = max ( (Entry - Low) / Entry ), для шорта = max( (High - Entry) / Entry ).
		/// </summary>
		public double MaxAdversePct { get; set; }

		/// <summary>
		/// Max favorable excursion по 1m-пути, в процентах от Entry (положительное число).
		/// Для лонга = max ( (High - Entry) / Entry ), для шорта = max( (Entry - Low) / Entry ).
		/// </summary>
		public double MaxFavorablePct { get; set; }
		}
	}
