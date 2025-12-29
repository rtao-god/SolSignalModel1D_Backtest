namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.StrategySimulators
	{
	/// <summary>
	/// Набор настраиваемых параметров сценарной стратегии.
	/// Вынесено отдельно, чтобы:
	/// - не держать "магические числа" внутри симулятора;
	/// - удобно подбирать параметры через несколько пресетов.
	/// </summary>
	public sealed class StrategyParameters
		{
		/// <summary>Человекочитаемое имя пресета (для логов).</summary>
		public string Name { get; init; } = "baseline";

		// --- Объёмы ног (в долларах, используются только для расчёта долей) ---

		/// <summary>Базовая нога (первый вход по направлению модели).</summary>
		public double BaseStakeUsd { get; init; } = 1350.0;

		/// <summary>Хедж/вторая нога (шорт против тренда и второй вход по тренду).</summary>
		public double HedgeStakeUsd { get; init; } = 2100.0;

		// --- Смещения по цене, в долларах от цены базового входа ---

		/// <summary>TP базовой позиции, пока второй лонг не открыт (сценарий 1).</summary>
		public double BaseTpOffsetUsd { get; init; } = 2.0;

		/// <summary>Триггер открытия хеджа против базовой позиции.</summary>
		public double HedgeTriggerOffsetUsd { get; init; } = 2.0;

		/// <summary>SL хеджа (когда рынок возвращается к цене входа и дальше).</summary>
		public double HedgeStopOffsetUsd { get; init; } = 1.0;

		/// <summary>TP хеджа (сценарий 2, когда рынок продолжает движение против базовой ноги).</summary>
		public double HedgeTpOffsetUsd { get; init; } = 3.0;

		/// <summary>SL второй ноги по тренду (после выбития хеджа, сценарий 4).</summary>
		public double SecondLegStopOffsetUsd { get; init; } = 1.0;

		/// <summary>
		/// TP для двойного входа (base + second) — цель, куда держим обе ноги,
		/// когда рынок «переобулся» и пошёл по направлению базовой позиции.
		/// </summary>
		public double DoublePositionTpOffsetUsd { get; init; } = 3.0;

		// --- Risk-management ---

		/// <summary>Стартовый баланс счёта.</summary>
		public double InitialBalanceUsd { get; init; } = 10_000.0;

		/// <summary>Доля капитала, рискуемая в одном дне (оба входа вместе).</summary>
		public double TotalRiskFractionPerTrade { get; init; } = 0.30;

		/// <summary>
		/// Удобный клон с частичным переопределением параметров.
		/// Позволяет не дублировать все поля во всех пресетах.
		/// </summary>
		public StrategyParameters With (
			string? name = null,
			double? baseStakeUsd = null,
			double? hedgeStakeUsd = null,
			double? baseTpOffsetUsd = null,
			double? hedgeTriggerOffsetUsd = null,
			double? hedgeStopOffsetUsd = null,
			double? hedgeTpOffsetUsd = null,
			double? secondLegStopOffsetUsd = null,
			double? doublePositionTpOffsetUsd = null,
			double? totalRiskFractionPerTrade = null )
			{
			// Здесь важно копировать всё из текущего экземпляра,
			// чтобы можно было наращивать изменения каскадом (baseline → best → вариации).
			return new StrategyParameters
				{
				Name = name ?? Name,

				BaseStakeUsd = baseStakeUsd ?? BaseStakeUsd,
				HedgeStakeUsd = hedgeStakeUsd ?? HedgeStakeUsd,

				BaseTpOffsetUsd = baseTpOffsetUsd ?? BaseTpOffsetUsd,
				HedgeTriggerOffsetUsd = hedgeTriggerOffsetUsd ?? HedgeTriggerOffsetUsd,
				HedgeStopOffsetUsd = hedgeStopOffsetUsd ?? HedgeStopOffsetUsd,
				HedgeTpOffsetUsd = hedgeTpOffsetUsd ?? HedgeTpOffsetUsd,
				SecondLegStopOffsetUsd = secondLegStopOffsetUsd ?? SecondLegStopOffsetUsd,
				DoublePositionTpOffsetUsd = doublePositionTpOffsetUsd ?? DoublePositionTpOffsetUsd,

				InitialBalanceUsd = InitialBalanceUsd,
				TotalRiskFractionPerTrade = totalRiskFractionPerTrade ?? TotalRiskFractionPerTrade
				};
			}

		/// <summary>Исторический базовый пресет — как стратегия была "из коробки".</summary>
		public static StrategyParameters Baseline => new StrategyParameters
			{
			Name = "baseline"
			};

		/// <summary>
		/// Набор пресетов для одного запуска.
		/// Здесь реализованы:
		/// - baseline (как было);
		/// - best (second_sl_1_50 + hedge_tp_4);
		/// - серия A: играем SecondLegStopOffsetUsd вокруг best;
		/// - серия B: двигаем DoublePositionTpOffsetUsd при фиксированном стопе второй ноги;
		/// - серия C: двигаем HedgeStakeUsd вокруг базового.
		/// </summary>
		public static IReadOnlyList<StrategyParameters> AllPresets
			{
			get
				{
				var baseline = Baseline;

				// Текущий лучший сетап по твоим результатам:
				// SecondLegStopOffsetUsd = 1.5, HedgeTpOffsetUsd = 4.0
				var best = baseline.With (
					name: "second_sl_1_50",
					secondLegStopOffsetUsd: 1.5,
					hedgeTpOffsetUsd: 4.0);

				return new[]
				{
					// 1. Исторический baseline (для сравнения)
					baseline,

					// 2. Текущий лучший сетап
					best,

					// --- Серия A: двигаем стоп второй ноги вокруг best ---
					best.With (
						name: "second_sl_1_25",
						secondLegStopOffsetUsd: 1.25 ),
					best.With (
						name: "second_sl_1_75",
						secondLegStopOffsetUsd: 1.75 ),

					// --- Серия B: двигаем TP двойной позиции при фиксированном стопе второй ноги ---
					best.With (
						name: "double_tp_4_second_sl_1_5",
						doublePositionTpOffsetUsd: 4.0 ),
					best.With (
						name: "double_tp_4_5_second_sl_1_5",
						doublePositionTpOffsetUsd: 4.5 ),
					best.With (
						name: "double_tp_5_second_sl_1_5",
						doublePositionTpOffsetUsd: 5.0 ),

					// --- Серия C: меняем размер хеджа вокруг базового значения ---
					best.With (
						name: "hedge_2000",
						hedgeStakeUsd: 2000.0 ),
					best.With (
						name: "hedge_2200",
						hedgeStakeUsd: 2200.0 ),
					best.With (
						name: "hedge_2500",
						hedgeStakeUsd: 2500.0 )
				};
				}
			}
		}
	}
