using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Итог по одному дню: что сказала дневная модель и что реально было.
	/// Это исходный твой класс + поля для delayed A/B.
	/// </summary>
	public sealed class PredictionRecord
		{
		public DateTime DateUtc { get; set; }

		/// <summary>
		/// Факт из DataRow.Label (0/1/2)
		/// </summary>
		public int TrueLabel { get; set; }

		/// <summary>
		/// Что предсказала дневная модель (0/1/2)
		/// </summary>
		public int PredLabel { get; set; }

		public bool PredMicroUp { get; set; }
		public bool PredMicroDown { get; set; }

		public bool FactMicroUp { get; set; }
		public bool FactMicroDown { get; set; }

		/// <summary>Цена входа (12:00 UTC)</summary>
		public double Entry { get; set; }

		/// <summary>Максимум за следующие 24h</summary>
		public double MaxHigh24 { get; set; }

		/// <summary>Минимум за следующие 24h</summary>
		public double MinLow24 { get; set; }

		/// <summary>Цена закрытия через 24h</summary>
		public double Close24 { get; set; }

		public bool RegimeDown { get; set; }

		public string Reason { get; set; } = string.Empty;

		public double MinMove { get; set; }

		// ==== ДОБАВЛЕНО для delayed A/B ====

		/// <summary>
		/// Источник идеи отложенного входа: "A" (глубокий дип) / "B" (мелкий откат) / null.
		/// </summary>
		public string? DelayedSource { get; set; }

		/// <summary>
		/// Модель вообще сказала "давай отложенный вход".
		/// </summary>
		public bool DelayedEntryUsed { get; set; }

		/// <summary>
		/// Цена реально дошла, вход был исполнен.
		/// </summary>
		public bool DelayedEntryExecuted { get; set; }

		/// <summary>
		/// Цена фактического входа по delayed.
		/// </summary>
		public double DelayedEntryPrice { get; set; }

		/// <summary>
		/// Результат внутридневного отработанного delayed (0 none, 1 TP, 2 SL, 3 amb).
		/// </summary>
		public int DelayedIntradayResult { get; set; }

		/// <summary>
		/// TP%, который мы ставили для delayed.
		/// </summary>
		public double DelayedIntradayTpPct { get; set; }

		/// <summary>
		/// SL%, который мы ставили для delayed.
		/// </summary>
		public double DelayedIntradaySlPct { get; set; }

		/// <summary>
		/// Что сказал таргетный слой (0 — ничего, 1 — мелкий, 2 — глубокий).
		/// </summary>
		public int TargetLevelClass { get; set; }
		}
	}
