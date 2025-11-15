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

		public string Source { get; set; } = "";   // Daily / DelayedA / DelayedB
		public string Bucket { get; set; } = "";   // daily / intraday / delayed
		}
	}
