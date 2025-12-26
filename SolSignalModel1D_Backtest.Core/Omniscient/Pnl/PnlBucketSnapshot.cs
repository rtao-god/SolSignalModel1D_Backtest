using System;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Итог по одной корзине (daily / intraday / delayed).
	/// Нужен только для отчёта.
	/// </summary>
	public sealed class PnlBucketSnapshot
		{
		public string Name { get; set; } = string.Empty;
		public double StartCapital { get; set; }
		public double EquityNow { get; set; }
		public double Withdrawn { get; set; }
		}
	}
