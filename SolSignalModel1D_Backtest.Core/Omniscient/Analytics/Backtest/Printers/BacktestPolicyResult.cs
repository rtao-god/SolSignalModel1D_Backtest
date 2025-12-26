using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Результаты бэктеста по одной политике и одному режиму.
	/// Хранит:
	/// - базовые метрики (PnL, max DD, ликвидации, withdrawn),
	/// - список сделок и снапшоты бакетов.
	/// </summary>
	public sealed class BacktestPolicyResult
		{
		public string PolicyName { get; set; } = string.Empty;
		public MarginMode Margin { get; set; }

		/// <summary>
		/// true, если политика была прогнана с anti-direction overlay.
		/// false, если это "обычный" baseline-режим.
		/// </summary>
		public bool UseAntiDirectionOverlay { get; set; }

		/// <summary>
		/// Все сделки по данной политике.
		/// В baseline-снапшоте они не сериализуются, но используются для агрегатов.
		/// </summary>
		public List<PnLTrade> Trades { get; set; } = new List<PnLTrade> ();

		// === из ComputePnL ===

		/// <summary>
		/// Итоговый PnL (в долях).
		/// </summary>
		public double TotalPnlPct { get; set; }

		/// <summary>
		/// Максимальная просадка (в долях).
		/// </summary>
		public double MaxDdPct { get; set; }

		/// <summary>
		/// Число сделок по источникам (baseline, delayedA / B и т.п.).
		/// </summary>
		public Dictionary<string, int> TradesBySource { get; set; } =
			new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Суммарно "вытащенные" средства (withdraw).
		/// </summary>
		public double WithdrawnTotal { get; set; }

		/// <summary>
		/// Снапшоты PnL-бакетов во времени.
		/// </summary>
		public List<PnlBucketSnapshot> BucketSnapshots { get; set; } = new List<PnlBucketSnapshot> ();

		/// <summary>
		/// Был ли факт ликвидации хотя бы один раз.
		/// </summary>
		public bool HadLiquidation { get; set; }
		}
	}
