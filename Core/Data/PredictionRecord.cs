using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public sealed class PredictionRecord
		{
		public DateTime DateUtc { get; set; }

		// Классификация
		public int TrueLabel { get; set; }
		public int PredLabel { get; set; }

		// Микро
		public bool PredMicroUp { get; set; }
		public bool PredMicroDown { get; set; }
		public bool FactMicroUp { get; set; }
		public bool FactMicroDown { get; set; }

		// Цены дня
		public double Entry { get; set; }
		public double MaxHigh24 { get; set; }
		public double MinLow24 { get; set; }
		public double Close24 { get; set; }

		// Контекст
		public bool RegimeDown { get; set; }
		public string Reason { get; set; } = string.Empty;
		public double MinMove { get; set; }

		// ===== Delayed A/B =====
		/// <summary>Источник: "A" или "B" (если применимо).</summary>
		public string? DelayedSource { get; set; }

		/// <summary>Мы вообще рассматривали отложенный вход в этот день?</summary>
		public bool DelayedEntryAsked { get; set; }

		/// <summary>Мы использовали отложенную логику при принятии решения?</summary>
		public bool DelayedEntryUsed { get; set; }

		/// <summary>Отложенный вход реально исполнился (была сделка)?</summary>
		public bool DelayedEntryExecuted { get; set; }

		/// <summary>Цена входа, если DelayedEntryExecuted == true.</summary>
		public double DelayedEntryPrice { get; set; }

		/// <summary>Интрадей-результат отложенной логики: см. enum DelayedIntradayResult.</summary>
		public int DelayedIntradayResult { get; set; }

		/// <summary>TP/SL проценты для отложенной логики.</summary>
		public double DelayedIntradayTpPct { get; set; }
		public double DelayedIntradaySlPct { get; set; }

		/// <summary>Класс таргета (если используется целевая модель уровня).</summary>
		public int TargetLevelClass { get; set; }

		/// <summary>Если asked=true, но executed=false — сюда пишем "почему не вошли".</summary>
		public string? DelayedWhyNot { get; set; }

		/// <summary>Фактическое время исполнения delayed (для корректного PnL по минуткам).</summary>
		public DateTime? DelayedEntryExecutedAtUtc { get; set; }

		// ===== SL online =====
		/// <summary>Вероятность SL (если считалась оффлайн/онлайн).</summary>
		public double SlProb { get; set; }

		/// <summary>Онлайн-решение SL: true = высокий риск, false = низкий.</summary>
		public bool SlHighDecision { get; set; }
		}
	}
