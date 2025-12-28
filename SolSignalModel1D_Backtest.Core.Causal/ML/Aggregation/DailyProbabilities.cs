namespace SolSignalModel1D_Backtest.Core.Causal.ML.Aggregation
	{
	/// <summary>
	/// Дневные вероятности трёх классов и вспомогательные флаги фильтров.
	/// Без логики, только контейнер значений.
	/// </summary>
	public struct DailyProbabilities
		{
		/// <summary>Вероятность движения вверх за сутки.</summary>
		public double PUp { get; set; }

		/// <summary>Вероятность плоского дня.</summary>
		public double PFlat { get; set; }

		/// <summary>Вероятность движения вниз за сутки.</summary>
		public double PDown { get; set; }

		/// <summary>
		/// Сводный показатель уверенности в тройке вероятностей.
		/// Конкретная формула (max(P*) или другая) задаётся позже.
		/// </summary>
		public double Confidence { get; set; }

		/// <summary>
		/// Флаг: BTC-фильтр запретил использовать сигнал "вверх" (дневной long).
		/// </summary>
		public bool BtcFilterBlockedUp { get; set; }

		/// <summary>
		/// Флаг: BTC-фильтр запретил использовать сигнал "flat".
		/// </summary>
		public bool BtcFilterBlockedFlat { get; set; }

		/// <summary>
		/// Флаг: BTC-фильтр запретил использовать сигнал "вниз" (дневной short).
		/// </summary>
		public bool BtcFilterBlockedDown { get; set; }
		}
	}
