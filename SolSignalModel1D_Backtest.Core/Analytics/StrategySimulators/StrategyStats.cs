namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
	{
	/// <summary>
	/// Агрегированная статистика по стратегии:
	/// - PnL по сценариям (1..4);
	/// - общие метрики по дням;
	/// - разрез по PredLabel;
	/// - серии по сценариям и по дням, где срабатывает SL хеджа.
	/// </summary>
	public sealed class StrategyStats
		{
		// --- Капитал и объёмы ---

		/// <summary>Стартовый баланс кошелька (в начале прогона).</summary>
		public double StartBalance { get; set; }

		/// <summary>Финальный баланс после всех сделок (учёт только убытков, профит выводится).</summary>
		public double EndBalance { get; set; }

		/// <summary>Минимальный баланс в процессе (для расчёта просадки).</summary>
		public double MinBalance { get; set; }

		/// <summary>Максимальная просадка в абсолюте (StartBalance - MinBalance).</summary>
		public double MaxDrawdownAbs { get; set; }

		/// <summary>Максимальная просадка в долях от стартового баланса.</summary>
		public double MaxDrawdownPct { get; set; }

		/// <summary>Общий риск на сделку в день 1 (в долларах).</summary>
		public double StartTotalStake { get; set; }

		/// <summary>Минимальный общий риск на сделку (в долларах) по мере падения капитала.</summary>
		public double MinTotalStake { get; set; }

		/// <summary>Сколько денег всего было выведено с профитных дней.</summary>
		public double TotalWithdrawnProfit { get; set; }

		// --- Общие метрики по дням ---

		/// <summary>Всего "дней-сделок", по которым удалось что-то посчитать.</summary>
		public int TradesCount { get; set; }

		/// <summary>Количество прибыльных дней (PnL &gt; 0).</summary>
		public int ProfitTradesCount { get; set; }

		/// <summary>Количество убыточных дней (PnL &lt; 0).</summary>
		public int LossTradesCount { get; set; }

		/// <summary>Суммарный чистый PnL по всем дням (profit + loss).</summary>
		public double TotalPnlNet { get; set; }

		/// <summary>Суммарный валовый профит (только плюс).</summary>
		public double TotalProfitGross { get; set; }

		/// <summary>Суммарный валовый убыток (отрицательное число).</summary>
		public double TotalLossGross { get; set; }

		// --- Сценарии (как в описании стратегии) ---

		public int Scenario1Count { get; set; }
		public double Scenario1Pnl { get; set; }

		public int Scenario2Count { get; set; }
		public double Scenario2Pnl { get; set; }

		public int Scenario3Count { get; set; }
		public double Scenario3Pnl { get; set; }

		public int Scenario4Count { get; set; }
		public double Scenario4Pnl { get; set; }

		// --- Серии по сценариям ---

		public int MaxScenario1Streak { get; set; }
		public int MaxScenario2Streak { get; set; }
		public int MaxScenario3Streak { get; set; }
		public int MaxScenario4Streak { get; set; }

		/// <summary>Максимальная серия дней, когда стоп шорта срабатывал (сценарии 3 или 4).</summary>
		public int MaxHedgeSlStreak { get; set; }

		// --- Разрез по PredLabel ---

		/// <summary>PredLabel = 2 (up).</summary>
		public int TotalPredUpCount { get; set; }
		public double TotalPredUpPnl { get; set; }

		/// <summary>PredLabel = 0 (down).</summary>
		public int TotalPredDownCount { get; set; }
		public double TotalPredDownPnl { get; set; }

		/// <summary>PredLabel = 1 (flat).</summary>
		public int TotalPredFlatCount { get; set; }
		public double TotalPredFlatPnl { get; set; }
		}
	}
