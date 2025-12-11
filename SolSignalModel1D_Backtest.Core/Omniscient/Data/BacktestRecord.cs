using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
	{
	/// <summary>
	/// Омнисциентная запись по одному дню:
	/// каузальная часть (решения модели на утро) + forward-исходы рынка.
	/// На этом уровне базовые поля считаются неизменяемыми.
	/// </summary>
	public sealed class BacktestRecord
		{
		/// <summary>
		/// Каузальный дневной прогноз (то, что было доступно на момент утра).
		/// </summary>
		public CausalPredictionRecord Causal { get; init; } = null!;

		/// <summary>
		/// Омнисциентные исходы forward-окна (Entry / High / Low / Close / путь и т.п.).
		/// </summary>
		public ForwardOutcomes Forward { get; init; } = null!;

		// ==================== Базовая идентичность дня ====================

		public DateTime DateUtc => Causal.DateUtc;

		// ==================== Day / Day+Micro / Total ====================

		public int TrueLabel => Causal.TrueLabel;
		public int PredLabel => Causal.PredLabel;
		public int PredLabel_Day => Causal.PredLabel_Day;
		public int PredLabel_DayMicro => Causal.PredLabel_DayMicro;
		public int PredLabel_Total => Causal.PredLabel_Total;

		public double ProbUp_Day => Causal.ProbUp_Day;
		public double ProbFlat_Day => Causal.ProbFlat_Day;
		public double ProbDown_Day => Causal.ProbDown_Day;

		public double ProbUp_DayMicro => Causal.ProbUp_DayMicro;
		public double ProbFlat_DayMicro => Causal.ProbFlat_DayMicro;
		public double ProbDown_DayMicro => Causal.ProbDown_DayMicro;

		public double ProbUp_Total => Causal.ProbUp_Total;
		public double ProbFlat_Total => Causal.ProbFlat_Total;
		public double ProbDown_Total => Causal.ProbDown_Total;

		public double Conf_Day => Causal.Conf_Day;
		public double Conf_Micro => Causal.Conf_Micro;

		// ==================== Микро-слой ====================

		public bool MicroPredicted => Causal.MicroPredicted;

		public bool PredMicroUp => Causal.PredMicroUp;
		public bool PredMicroDown => Causal.PredMicroDown;

		public bool FactMicroUp => Causal.FactMicroUp;
		public bool FactMicroDown => Causal.FactMicroDown;

		// ==================== Контекст режима ====================

		public bool RegimeDown => Causal.RegimeDown;
		public string Reason => Causal.Reason;

		/// <summary>
		/// MinMove, использованный в каузальной разметке/решениях.
		/// </summary>
		public double MinMove => Causal.MinMove;

		// ==================== Forward-окно (24h диапазон) ====================

		public double Entry => Forward.Entry;
		public double MaxHigh24 => Forward.MaxHigh24;
		public double MinLow24 => Forward.MinLow24;
		public double Close24 => Forward.Close24;
		public double ForwardMinMove => Forward.MinMove;
		public DateTime WindowEndUtc => Forward.WindowEndUtc;

		public IReadOnlyList<Candle1m> DayMinutes => Forward.DayMinutes;

		// ==================== SL-модель (каузальный уровень) ====================

		public double SlProb => Causal.SlProb;
		public bool SlHighDecision => Causal.SlHighDecision;
		public double Conf_SlLong => Causal.Conf_SlLong;
		public double Conf_SlShort => Causal.Conf_SlShort;

		// ==================== Delayed-слой (каузальные параметры) ====================

		public string? DelayedSource => Causal.DelayedSource;
		public bool DelayedEntryAsked => Causal.DelayedEntryAsked;
		public bool DelayedEntryUsed => Causal.DelayedEntryUsed;
		public double DelayedIntradayTpPct => Causal.DelayedIntradayTpPct;
		public double DelayedIntradaySlPct => Causal.DelayedIntradaySlPct;
		public int TargetLevelClass => Causal.TargetLevelClass;

		// ==================== Omniscient-метки PnL/Delayed ====================

		/// <summary>
		/// Anti-direction overlay был применён для этого дня в рамках конкретной политики.
		/// </summary>
		public bool AntiDirectionApplied { get; set; }

		/// <summary>
		/// Факт исполнения delayed-входа (омнисциентный уровень).
		/// </summary>
		public bool DelayedEntryExecuted { get; set; }

		/// <summary>
		/// Цена входа по delayed-сделке (если была исполнена).
		/// </summary>
		public double DelayedEntryPrice { get; set; }

		/// <summary>
		/// Время исполнения delayed-сделки.
		/// </summary>
		public DateTime? DelayedEntryExecutedAtUtc { get; set; }

		/// <summary>
		/// Результат delayed-интрадей сделки (произвольный класс/код).
		/// </summary>
		public int DelayedIntradayResult { get; set; }

		/// <summary>
		/// Диагностическое объяснение, почему delayed не был исполнен или был отменён.
		/// </summary>
		public string? DelayedWhyNot { get; set; }
		}
	}
