using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Результат каузального inference для одного дня:
	/// вероятности слоёв (Day / Day+Micro) и итоговые вероятности после runtime-оверлеев (SL/Delayed и т.п.).
	///
	/// Ключевой контракт:
	/// - Day/Day+Micro/Total вероятности всегда заданы (это рабочий продукт пайплайна).
	/// - SL/Delayed могут отсутствовать: в таком случае поля = null (а не "0/false"), чтобы нельзя было
	///   незаметно интерпретировать "нет данных" как валидное значение.
	/// </summary>
	public sealed class CausalPredictionRecord
		{
		public DateTime DateUtc { get; init; }

		// ===== Выбранные классы по слоям =====
		public int PredLabel { get; init; }
		public int PredLabel_Day { get; init; }
		public int PredLabel_DayMicro { get; init; }

		// Total - runtime слой: может пересчитываться оверлеями.
		public int PredLabel_Total { get; set; }

		// ===== Вероятности Day =====
		public double ProbUp_Day { get; init; }
		public double ProbFlat_Day { get; init; }
		public double ProbDown_Day { get; init; }

		// ===== Вероятности Day+Micro =====
		public double ProbUp_DayMicro { get; init; }
		public double ProbFlat_DayMicro { get; init; }
		public double ProbDown_DayMicro { get; init; }

		// ===== Итоговые вероятности (после всех runtime-оверлеев) =====
		public double ProbUp_Total { get; set; }
		public double ProbFlat_Total { get; set; }
		public double ProbDown_Total { get; set; }

		// ===== Конфиденсы/сигналы =====
		public double Conf_Day { get; init; }
		public double Conf_Micro { get; init; }

		// ===== Микро-слой: только то, что модель решила использовать =====
		public bool MicroPredicted { get; init; }
		public bool PredMicroUp { get; init; }
		public bool PredMicroDown { get; init; }

		// ===== Контекст/диагностика принятого решения =====
		public bool RegimeDown { get; init; }
		public string Reason { get; init; } = string.Empty;
		public double MinMove { get; init; }

		// ===== SL-слой (runtime overlay) =====
		// null = SL overlay не считался/не применялся (важно: это не "0%").
		public double? SlProb { get; set; }
		public bool? SlHighDecision { get; set; }
		public double? Conf_SlLong { get; set; }
		public double? Conf_SlShort { get; set; }

		// ===== Delayed-слой (runtime параметры/результат) =====
		// null = Delayed overlay не считался/не применялся.
		public string? DelayedSource { get; set; }
		public bool? DelayedEntryAsked { get; set; }
		public bool? DelayedEntryUsed { get; set; }
		public double? DelayedIntradayTpPct { get; set; }
		public double? DelayedIntradaySlPct { get; set; }

		// Если это относится к delayed/уровням таргета — тоже не должен быть "0 по умолчанию".
		public int? TargetLevelClass { get; set; }

		// ===== Явные "OrThrow" аксессоры =====
		// Нужны, чтобы код падал в неправильном состоянии.
		public double GetSlProbOrThrow ()
			{
			if (SlProb is null)
				throw new InvalidOperationException ($"[causal] SL not evaluated for {DateUtc:O}, but SlProb requested.");
			return SlProb.Value;
			}

		public (double TpPct, double SlPct) GetDelayedTpSlOrThrow ()
			{
			if (DelayedIntradayTpPct is null || DelayedIntradaySlPct is null)
				throw new InvalidOperationException ($"[causal] Delayed not evaluated for {DateUtc:O}, but TP/SL requested.");
			return (DelayedIntradayTpPct.Value, DelayedIntradaySlPct.Value);
			}
		}
	}
