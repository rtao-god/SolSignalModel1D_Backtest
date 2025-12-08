using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public sealed class PredictionRecord
		{
		public DateTime DateUtc { get; set; }

		// ===== Классификация (факт + финальный класс, плюс раздельные слои) =====

		/// <summary>Фактический дневной класс таргета: 0=down, 1=flat, 2=up.</summary>
		public int TrueLabel { get; set; }

		/// <summary>
		/// Финальный предсказанный класс, который использует текущий пайплайн PnL.
		/// Семантика может отличаться от PredLabel_Day/Total и будет постепенно
		/// выравниваться по мере миграции.
		/// </summary>
		public int PredLabel { get; set; }

		/// <summary>Предсказанный класс только дневной модели (на основе P_day).</summary>
		public int PredLabel_Day { get; set; }

		/// <summary>Предсказанный класс после учёта микро-слоя (Day+Micro).</summary>
		public int PredLabel_DayMicro { get; set; }

		/// <summary>Предсказанный класс после полного стека (Day+Micro+SL).</summary>
		public int PredLabel_Total { get; set; }

		// ===== Вероятности слоёв (Day / Day+Micro / Total) =====

		/// <summary>Базовая дневная вероятность класса "Up".</summary>
		public double ProbUp_Day { get; set; }

		/// <summary>Базовая дневная вероятность класса "Flat".</summary>
		public double ProbFlat_Day { get; set; }

		/// <summary>Базовая дневная вероятность класса "Down".</summary>
		public double ProbDown_Day { get; set; }

		/// <summary>Вероятность "Up" после учёта микро-слоя (Day+Micro).</summary>
		public double ProbUp_DayMicro { get; set; }

		/// <summary>Вероятность "Flat" после учёта микро-слоя (Day+Micro).</summary>
		public double ProbFlat_DayMicro { get; set; }

		/// <summary>Вероятность "Down" после учёта микро-слоя (Day+Micro).</summary>
		public double ProbDown_DayMicro { get; set; }

		/// <summary>Финальная вероятность "Up" после учёта микро и SL (Total).</summary>
		public double ProbUp_Total { get; set; }

		/// <summary>Финальная вероятность "Flat" после учёта микро и SL (Total).</summary>
		public double ProbFlat_Total { get; set; }

		/// <summary>Финальная вероятность "Down" после учёта микро и SL (Total).</summary>
		public double ProbDown_Total { get; set; }

		/// <summary>Уверенность дневной модели (Day-слой, например max P_day).</summary>
		public double Conf_Day { get; set; }

		/// <summary>Уверенность микро-слоя, использованного в агрегации.</summary>
		public double Conf_Micro { get; set; }

		/// <summary>Уверенность SL-модели для длинной позиции.</summary>
		public double Conf_SlLong { get; set; }

		/// <summary>Уверенность SL-модели для короткой позиции.</summary>
		public double Conf_SlShort { get; set; }

		// ===== Микро-факт / микро-прогноз (как и раньше) =====

		public bool PredMicroUp { get; set; }
		public bool PredMicroDown { get; set; }
		public bool FactMicroUp { get; set; }
		public bool FactMicroDown { get; set; }

		// ===== Цены дня =====

		public double Entry { get; set; }
		public double MaxHigh24 { get; set; }
		public double MinLow24 { get; set; }
		public double Close24 { get; set; }

		// ===== Контекст =====

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

		/// <summary>Если asked=true, но executed=false — сюда пишем причину отказа от входа.</summary>
		public string? DelayedWhyNot { get; set; }

		/// <summary>Фактическое время исполнения delayed (для корректного PnL по минуткам).</summary>
		public DateTime? DelayedEntryExecutedAtUtc { get; set; }

		// ===== SL online =====

		/// <summary>Вероятность SL (если считалась оффлайн/онлайн).</summary>
		public double SlProb { get; set; }

		/// <summary>Онлайн-решение SL: true = высокий риск, false = низкий.</summary>
		public bool SlHighDecision { get; set; }

		// ===== Anti-direction (PnL overlay) =====

		/// <summary>
		/// Флаг, что в PnL для этого дня направление сделки было перевёрнуто (Anti-D).
		/// Классификация при этом остаётся прежней (PredLabel / PredLabel_Total).
		/// </summary>
		public bool AntiDirectionApplied { get; set; }
		}
	}
