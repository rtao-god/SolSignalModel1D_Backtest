using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
{
	/// <summary>
	/// Конфигурация бэктеста:
	/// - общие параметры (SL/TP);
	/// - набор политик плеча и маржи.
	/// </summary>
	public sealed class BacktestConfig
		{
		/// <summary>
		/// Дневной стоп по equity в пределах дня (в долях, 0.05 = 5%).
		/// Прокидывается в PnL-движок как DailyStopPct.
		/// </summary>
		public double DailyStopPct { get; init; }

		/// <summary>
		/// Дневной тейк-профит по equity в пределах дня (в долях, 0.03 = 3%).
		/// Прокидывается в PnL-движок как DailyTpPct.
		/// </summary>
		public double DailyTpPct { get; init; }

		/// <summary>
		/// Набор политик плеча и маржи, которые будут прогоняться в бэктесте.
		/// </summary>
		public List<PolicyConfig> Policies { get; init; } = new ();
		}

	/// <summary>
	/// Логическое описание одной политики плеча для бэктеста.
	/// Не содержит ссылок на реальные ILeveragePolicy, только тип/параметры.
	/// </summary>
	public sealed class PolicyConfig
		{
		/// <summary>
		/// Человекочитаемое имя политики (идентификатор в отчётах).
		/// </summary>
		public string Name { get; init; } = string.Empty;

		/// <summary>
		/// Тип политики:
		/// - "const"      → LeveragePolicies.ConstPolicy;
		/// - "risk_aware" → LeveragePolicies.RiskAwarePolicy;
		/// - "ultra_safe" → LeveragePolicies.UltraSafePolicy.
		/// </summary>
		public string PolicyType { get; init; } = string.Empty;

		/// <summary>
		/// Базовое плечо для типов, которые используют константу (например, "const").
		/// Для других типов может быть null.
		/// </summary>
		public double? Leverage { get; init; }

		/// <summary>
		/// Режим маржи для этой политики (Cross/Isolated).
		/// </summary>
		public MarginMode MarginMode { get; init; }
		}
	}
