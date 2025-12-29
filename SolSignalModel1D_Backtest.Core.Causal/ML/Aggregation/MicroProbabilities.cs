namespace SolSignalModel1D_Backtest.Core.Causal.ML.Aggregation
	{
	/// <summary>
	/// DTO для микро-слоя: условные вероятности и уверенность.
	/// </summary>
	public struct MicroProbabilities
		{
		/// <summary>Есть ли вообще валидный микро-прогноз.</summary>
		public bool HasPrediction { get; set; }

		/// <summary>
		/// Условная вероятность "микро-лонга" при базовом дневном классе Flat.
		/// </summary>
		public double PUpGivenFlat { get; set; }

		/// <summary>
		/// Условная вероятность "микро-шорта" при базовом дневном классе Flat.
		/// </summary>
		public double PDownGivenFlat { get; set; }

		/// <summary>
		/// Сводная уверенность микро-слоя (для агрегации).
		/// </summary>
		public double Confidence { get; set; }

		/// <summary>
		/// Предсказанный класс микро-слоя в собственной кодировке.
		/// Используется для диагностики, а не для формул.
		/// </summary>
		public int PredLabel { get; set; }
		}
	}
