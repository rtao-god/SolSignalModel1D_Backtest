using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Результат каузального inference для одного дня:
	/// вероятности слоёв (Day / Day+Micro) и итоговые вероятности после runtime-оверлеев (SL/Delayed и т.п.).
	///
	/// Контракты:
	/// - Day/Day+Micro/Total вероятности всегда заданы (рабочий продукт пайплайна).
	/// - SL/Delayed могут отсутствовать: в таком случае поля = null (а не "0/false"), чтобы нельзя было
	///   незаметно интерпретировать "нет данных" как валидное значение.
	///
	/// Важно для ML:
	/// - FeaturesVector — канонический числовой вектор фиксированной длины (под MlSchema.FeatureCount).
	///   Если он не задан/не той длины — тренировки/диагностика должны падать (это ошибка пайплайна).
	///
	/// Features (nullable) — это опциональный человекочитаемый контекст/feature-set.
	/// Он не обязан существовать, и не должен “маскировать” отсутствие ML-вектора.
	/// </summary>
	public sealed class CausalPredictionRecord
		{
		public DateTime DateUtc { get; init; }

		// ===== ML feature vector (канонический вход для ML.NET) =====
		// Длина обязана соответствовать MlSchema.FeatureCount (проверяется в MlTrainingUtils.ToFloatFixed).
		public ReadOnlyMemory<double> FeaturesVector { get; init; }

		// ===== Feature set (входы/контекст) =====
		// null = фичи не собирались или отсутствуют (например, нет истории).
		public CausalFeatures? Features { get; init; }

		// Прокси-свойства для обратной совместимости (Features?.X).
		// Они ничего не “додумывают”: missing остаётся null.
		public double? AtrPct => Features?.AtrPct;
		public double? DynVol => Features?.DynVol;

		public double? AltFracPos6h => Features?.AltFracPos6h;
		public double? AltFracPos24h => Features?.AltFracPos24h;
		public double? AltMedian24h => Features?.AltMedian24h;
		public bool? AltReliable => Features?.AltReliable;

		public double? SolRet30 => Features?.SolRet30;
		public double? SolRet3 => Features?.SolRet3;
		public double? SolRet1 => Features?.SolRet1;

		public double? BtcRet1 => Features?.BtcRet1;
		public double? BtcRet30 => Features?.BtcRet30;
		public double? BtcVs200 => Features?.BtcVs200;

		public double? SolEma50vs200 => Features?.SolEma50vs200;
		public double? BtcEma50vs200 => Features?.BtcEma50vs200;

		public double? Fng => Features?.Fng;
		public double? DxyChg30 => Features?.DxyChg30;
		public double? GoldChg30 => Features?.GoldChg30;
		public double? SolRsiCentered => Features?.SolRsiCentered;
		public double? RsiSlope3 => Features?.RsiSlope3;

		public bool? IsMorning => Features?.IsMorning;

		public double? LiqUpRel => Features?.LiqUpRel;
		public double? LiqDownRel => Features?.LiqDownRel;
		public double? FiboUpRel => Features?.FiboUpRel;
		public double? FiboDownRel => Features?.FiboDownRel;

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

		// ===== Микро-слой =====
		public bool MicroPredicted { get; init; }
		public bool PredMicroUp { get; init; }
		public bool PredMicroDown { get; init; }

		// ===== Контекст/диагностика =====
		public bool RegimeDown { get; init; }
		public string Reason { get; init; } = string.Empty;
		public double MinMove { get; init; }

		// ===== SL-слой (runtime overlay) =====
		public double? SlProb { get; set; }
		public bool? SlHighDecision { get; set; }
		public double? Conf_SlLong { get; set; }
		public double? Conf_SlShort { get; set; }

		// ===== Delayed-слой (runtime) =====
		public string? DelayedSource { get; set; }
		public bool? DelayedEntryAsked { get; set; }
		public bool? DelayedEntryUsed { get; set; }
		public double? DelayedIntradayTpPct { get; set; }
		public double? DelayedIntradaySlPct { get; set; }
		public int? TargetLevelClass { get; set; }

		// ===== Явные "OrThrow" аксессоры =====
		public double GetSlProbOrThrow ()
			{
			if (SlProb is null)
				throw new InvalidOperationException ($"[causal] SL not evaluated for {DateUtc:O}, but SlProb requested.");
			return SlProb.Value;
			}

		public bool GetSlHighDecisionOrThrow ()
			{
			if (SlHighDecision is null)
				throw new InvalidOperationException ($"[causal] SL not evaluated for {DateUtc:O}, but SlHighDecision requested.");
			return SlHighDecision.Value;
			}

		public (double TpPct, double SlPct) GetDelayedTpSlOrThrow ()
			{
			if (DelayedIntradayTpPct is null || DelayedIntradaySlPct is null)
				throw new InvalidOperationException ($"[causal] Delayed not evaluated for {DateUtc:O}, but TP/SL requested.");
			return (DelayedIntradayTpPct.Value, DelayedIntradaySlPct.Value);
			}

		public double GetFeatureOrThrow ( double? v, string featureName )
			{
			if (v is null)
				throw new InvalidOperationException ($"[causal] Feature '{featureName}' missing for {DateUtc:O}.");
			return v.Value;
			}

		public bool GetFeatureOrThrow ( bool? v, string featureName )
			{
			if (v is null)
				throw new InvalidOperationException ($"[causal] Feature '{featureName}' missing for {DateUtc:O}.");
			return v.Value;
			}
		}
	}
