using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
	{
	/// <summary>
	/// Omniscient-объект одного дня:
	/// каузальный прогноз (Causal) + forward-исходы (Forward) + omniscient-поля PnL/Delayed.
	/// </summary>
	public sealed class BacktestRecord
		{
		public BacktestRecord ()
			{
			// Инициализация по умолчанию, чтобы прокси-свойства могли писать в Causal/Forward,
			// даже если вызывающий код не устанавливает их явно через объектный инициализатор.
			Causal = new CausalPredictionRecord ();
			Forward = new ForwardOutcomes ();
			}

		/// <summary>
		/// Каузальная часть (то, что доступно в момент решения).
		/// </summary>
		public CausalPredictionRecord Causal { get; init; }

		/// <summary>
		/// Forward-часть (entry, 24h-диапазон, 1m-путь, forward-MinMove и т.п.).
		/// </summary>
		public ForwardOutcomes Forward { get; init; }

		/// <summary>
		/// Дата торгового дня / baseline-окна (UTC).
		/// Каноничный источник — Causal.DateUtc.
		/// </summary>
		public DateTime DateUtc
			{
			get => Causal.DateUtc;
			set => Causal.DateUtc = value;
			}

		/// <summary>
		/// Цена входа в baseline-окне (по ней считается дневная сделка).
		/// Каноничный источник — Forward.Entry.
		/// </summary>
		public double Entry
			{
			get => Forward.Entry;
			set => Forward.Entry = value;
			}

		// ======================= Day / Day+Micro / Total =======================

		public int TrueLabel
			{
			get => Causal.TrueLabel;
			set => Causal.TrueLabel = value;
			}

		public int PredLabel
			{
			get => Causal.PredLabel;
			set => Causal.PredLabel = value;
			}

		public int PredLabel_Day
			{
			get => Causal.PredLabel_Day;
			set => Causal.PredLabel_Day = value;
			}

		public int PredLabel_DayMicro
			{
			get => Causal.PredLabel_DayMicro;
			set => Causal.PredLabel_DayMicro = value;
			}

		public int PredLabel_Total
			{
			get => Causal.PredLabel_Total;
			set => Causal.PredLabel_Total = value;
			}

		public double ProbUp_Day
			{
			get => Causal.ProbUp_Day;
			set => Causal.ProbUp_Day = value;
			}

		public double ProbFlat_Day
			{
			get => Causal.ProbFlat_Day;
			set => Causal.ProbFlat_Day = value;
			}

		public double ProbDown_Day
			{
			get => Causal.ProbDown_Day;
			set => Causal.ProbDown_Day = value;
			}

		public double ProbUp_DayMicro
			{
			get => Causal.ProbUp_DayMicro;
			set => Causal.ProbUp_DayMicro = value;
			}

		public double ProbFlat_DayMicro
			{
			get => Causal.ProbFlat_DayMicro;
			set => Causal.ProbFlat_DayMicro = value;
			}

		public double ProbDown_DayMicro
			{
			get => Causal.ProbDown_DayMicro;
			set => Causal.ProbDown_DayMicro = value;
			}

		public double ProbUp_Total
			{
			get => Causal.ProbUp_Total;
			set => Causal.ProbUp_Total = value;
			}

		public double ProbFlat_Total
			{
			get => Causal.ProbFlat_Total;
			set => Causal.ProbFlat_Total = value;
			}

		public double ProbDown_Total
			{
			get => Causal.ProbDown_Total;
			set => Causal.ProbDown_Total = value;
			}

		public double Conf_Day
			{
			get => Causal.Conf_Day;
			set => Causal.Conf_Day = value;
			}

		public double Conf_Micro
			{
			get => Causal.Conf_Micro;
			set => Causal.Conf_Micro = value;
			}

		// ======================= Micro-слой =======================

		public bool PredMicroUp
			{
			get => Causal.PredMicroUp;
			set => Causal.PredMicroUp = value;
			}

		public bool PredMicroDown
			{
			get => Causal.PredMicroDown;
			set => Causal.PredMicroDown = value;
			}

		public bool FactMicroUp
			{
			get => Causal.FactMicroUp;
			set => Causal.FactMicroUp = value;
			}

		public bool FactMicroDown
			{
			get => Causal.FactMicroDown;
			set => Causal.FactMicroDown = value;
			}

		// ======================= Контекст режима =======================

		public bool RegimeDown
			{
			get => Causal.RegimeDown;
			set => Causal.RegimeDown = value;
			}

		public string Reason
			{
			get => Causal.Reason;
			set => Causal.Reason = value;
			}

		// ======================= Forward-окно (24h диапазон) =======================

		/// <summary>
		/// Forward-оценка дневной волатильности вида max(|High/Entry−1|, |Low/Entry−1|).
		/// Считается в ForwardOutcomes и используется Anti-D / SL.
		/// </summary>
		/// <summary>
		public double MinMove
			{
			get => Forward.MinMove;
			set => Forward.MinMove = value;
			}

		public double MaxHigh24
			{
			get => Forward.MaxHigh24;
			set => Forward.MaxHigh24 = value;
			}

		public double MinLow24
			{
			get => Forward.MinLow24;
			set => Forward.MinLow24 = value;
			}

		public double Close24
			{
			get => Forward.Close24;
			set => Forward.Close24 = value;
			}

		public DateTime WindowEndUtc
			{
			get => Forward.WindowEndUtc;
			set => Forward.WindowEndUtc = value;
			}

		public List<Candle1m> DayMinutes
			{
			get => Forward.DayMinutes;
			set => Forward.DayMinutes = value;
			}

		// ======================= SL-модель (каузальная часть) =======================

		/// <summary>Вероятность "SL будет первым" (positive-class SL-модели).</summary>
		public double SlProb
			{
			get => Causal.SlProb;
			set => Causal.SlProb = value;
			}

		/// <summary>Флаг high-risk дня по SL-модели.</summary>
		public bool SlHighDecision
			{
			get => Causal.SlHighDecision;
			set => Causal.SlHighDecision = value;
			}

		public double Conf_SlLong
			{
			get => Causal.Conf_SlLong;
			set => Causal.Conf_SlLong = value;
			}

		public double Conf_SlShort
			{
			get => Causal.Conf_SlShort;
			set => Causal.Conf_SlShort = value;
			}

		// ======================= Delayed-слой (каузальная часть) =======================

		/// <summary>Источник delayed-входа ("A", "B" или null).</summary>
		public string? DelayedSource
			{
			get => Causal.DelayedSource;
			set => Causal.DelayedSource = value;
			}

		/// <summary>Флаг, что delayed-вход запрашивался стратегией.</summary>
		public bool DelayedEntryAsked
			{
			get => Causal.DelayedEntryAsked;
			set => Causal.DelayedEntryAsked = value;
			}

		/// <summary>Флаг, что delayed-вход был принят к использованию (после всех гейтов).</summary>
		public bool DelayedEntryUsed
			{
			get => Causal.DelayedEntryUsed;
			set => Causal.DelayedEntryUsed = value;
			}

		/// <summary>TP-порог для delayed-сделки (в долях, 0.01 == 1%).</summary>
		public double DelayedIntradayTpPct
			{
			get => Causal.DelayedIntradayTpPct;
			set => Causal.DelayedIntradayTpPct = value;
			}

		/// <summary>SL-порог для delayed-сделки (в долях).</summary>
		public double DelayedIntradaySlPct
			{
			get => Causal.DelayedIntradaySlPct;
			set => Causal.DelayedIntradaySlPct = value;
			}

		/// <summary>Класс уровня таргета (если delayed-модель его использует).</summary>
		public int TargetLevelClass
			{
			get => Causal.TargetLevelClass;
			set => Causal.TargetLevelClass = value;
			}

		// ======================= Delayed-слой (omniscient часть) =======================

		/// <summary>
		/// Факт исполнения delayed-входа на 1m-пути.
		/// Это уже чисто omniscient-информация.
		/// </summary>
		public bool DelayedEntryExecuted { get; set; }

		/// <summary>Фактическая цена исполнения delayed-входа.</summary>
		public double DelayedEntryPrice { get; set; }

		/// <summary>Фактическое время исполнения delayed-входа (UTC).</summary>
		public DateTime? DelayedEntryExecutedAtUtc { get; set; }

		/// <summary>
		/// Исход intraday-пути для delayed-сделки (enum: None/TpFirst/SlFirst и т.п.).
		/// </summary>
		public int DelayedIntradayResult { get; set; }

		/// <summary>
		/// Диагностическая причина, почему delayed-вход не был исполнен
		/// (например, не достигнут уровень, конфликт с дневной стоп-политикой и т.п.).
		/// </summary>
		public string? DelayedWhyNot { get; set; }

		// ======================= Omniscient-метки PnL =======================

		/// <summary>
		/// Флаг, что для дня был применён Anti-direction overlay.
		/// Выставляется PnL-движком; потом может использоваться в отчётах.
		/// </summary>
		public bool AntiDirectionApplied { get; set; }
		}
	}
