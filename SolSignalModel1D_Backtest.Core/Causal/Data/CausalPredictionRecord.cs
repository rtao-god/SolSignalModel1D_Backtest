using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Каузальный дневной прогноз по одной утренней точке.
	/// Здесь только то, что доступно в момент принятия решения
	/// (плюс train-time лейблы/диагностика, не используемые в рантайме).
	/// </summary>
	public sealed class CausalPredictionRecord
		{
		/// <summary>
		/// Дата торгового дня / baseline-окна (UTC).
		/// Совпадает с Date у DataRow.
		/// </summary>
		public DateTime DateUtc { get; set; }

		/// <summary>
		/// Фактический дневной класс (0/1/2), рассчитанный path-based разметкой.
		/// Нужен только для оффлайн-метрик, в рантайме не используется.
		/// </summary>
		public int TrueLabel { get; set; }

		/// <summary>
		/// Финальный класс дневного слоя (то, что возвращает PredictionEngine.Class).
		/// Используется в PnL режимах DayOnly.
		/// </summary>
		public int PredLabel { get; set; }

		/// <summary>Класс чисто дневной модели (без микро, без SL).</summary>
		public int PredLabel_Day { get; set; }

		/// <summary>Класс после применения микро-оверлея (Day+Micro).</summary>
		public int PredLabel_DayMicro { get; set; }

		/// <summary>
		/// Класс после полного стека Day+Micro+SL.
		/// Технически это тот же argmax, что и PredLabel; свойство оставлено для совместимости.
		/// </summary>
		public int PredLabel_Total
			{
			get => PredLabel;
			set => PredLabel = value;
			}

		// ===== Вероятности по слоям =====

		public double ProbUp_Day { get; set; }
		public double ProbFlat_Day { get; set; }
		public double ProbDown_Day { get; set; }

		public double ProbUp_DayMicro { get; set; }
		public double ProbFlat_DayMicro { get; set; }
		public double ProbDown_DayMicro { get; set; }

		public double ProbUp_Total { get; set; }
		public double ProbFlat_Total { get; set; }
		public double ProbDown_Total { get; set; }

		/// <summary>Уверенность дневной модели (например, max(P_up, P_flat, P_down)).</summary>
		public double Conf_Day { get; set; }

		/// <summary>Условная уверенность микро-слоя.</summary>
		public double Conf_Micro { get; set; }

		// ===== Микро-слой =====

		/// <summary>Был ли вообще валидный микро-прогноз.</summary>
		public bool MicroPredicted { get; set; }

		/// <summary>Микро-сигнал "за up" внутри flat-дня.</summary>
		public bool PredMicroUp { get; set; }

		/// <summary>Микро-сигнал "за down" внутри flat-дня.</summary>
		public bool PredMicroDown { get; set; }

		/// <summary>Фактический признак микро-up (из DataRow), только для диагностики.</summary>
		public bool FactMicroUp { get; set; }

		/// <summary>Фактический признак микро-down (из DataRow), только для диагностики.</summary>
		public bool FactMicroDown { get; set; }

		// ===== Контекст =====

		/// <summary>Флаг даун-режима по Regime-модели, использованный при выборе dir-ветки.</summary>
		public bool RegimeDown { get; set; }

		/// <summary>Человеко-читаемое объяснение (ветка move/dir, BTC-фильтр и т.п.).</summary>
		public string Reason { get; set; } = string.Empty;

		/// <summary>
		/// MinMove из DataRow, использованный в разметке таргета и SL-оффлайне.
		/// Это train-time оценка, forward-MinMove живёт в omniscient-части.
		/// </summary>
		public double MinMove { get; set; }

		// ===== SL-модель =====

		/// <summary>Вероятность "SL будет первым" (positive-class SL-модели).</summary>
		public double SlProb { get; set; }

		/// <summary>
		/// Флаг high-risk дня по SL-модели (p &gt;= threshold и PredictedLabel == SL).
		/// Используется как гейт для delayed A/B и Anti-D.
		/// </summary>
		public bool SlHighDecision { get; set; }

		/// <summary>Опциональные confidence-метрики для поясняющих логов.</summary>
		public double Conf_SlLong { get; set; }
		public double Conf_SlShort { get; set; }

		// ===== Delayed-слой (intraday A/B, каузальная часть) =====

		/// <summary>Источник delayed-входа ("A", "B" или null).</summary>
		public string? DelayedSource { get; set; }

		/// <summary>Флаг, что delayed-вход запрашивался стратегией.</summary>
		public bool DelayedEntryAsked { get; set; }

		/// <summary>Флаг, что delayed-вход был принят к использованию (после всех гейтов).</summary>
		public bool DelayedEntryUsed { get; set; }

		/// <summary>TP-порог для delayed-сделки (в долях, 0.01 == 1%).</summary>
		public double DelayedIntradayTpPct { get; set; }

		/// <summary>SL-порог для delayed-сделки (в долях).</summary>
		public double DelayedIntradaySlPct { get; set; }

		/// <summary>Класс уровня таргета (если delayed-модель его использует).</summary>
		public int TargetLevelClass { get; set; }
		}
	}
