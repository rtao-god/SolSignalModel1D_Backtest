namespace SolSignalModel1D_Backtest.Core.Causal.ML.Aggregation
	{
	/// <summary>
	/// Конфигурация для агрегации вероятностей:
	/// веса микро-слоя, веса SL и правила клампа top-класса.
	/// Здесь только параметры; формулы будут реализованы отдельно.
	/// </summary>
	public sealed class ProbabilityAggregationConfig
		{
		// ===== Микро-слой =====

		/// <summary>
		/// Базовый множитель влияния микро-слоя (масштаб правки дневных вероятностей).
		/// </summary>
		public double BetaMicro { get; set; } = 1.0;

		/// <summary>
		/// Ограничение на максимальное изменение дневных вероятностей из-за микро-слоя.
		/// Например, 0.3 означает, что микро-слой не может изменить вероятность класса
		/// больше, чем на 0.3 в абсолютном значении.
		/// </summary>
		public double MaxMicroImpact { get; set; } = 0.3;

		/// <summary>
		/// Минимальная уверенность микро-слоя, при которой его сигнал вообще учитывается.
		/// </summary>
		public double MicroMinConfidence { get; set; } = 0.55;

		/// <summary>
		/// Порог "сильной" уверенности микро-слоя (для более агрессивной правки).
		/// </summary>
		public double MicroStrongConfidence { get; set; } = 0.70;

		// ===== SL-слой =====

		/// <summary>
		/// Базовый множитель влияния SL-слоя на финальные вероятности.
		/// </summary>
		public double GammaSl { get; set; } = 1.0;

		/// <summary>
		/// Ограничение на максимальное влияние SL-слоя на top-класс.
		/// </summary>
		public double MaxSlImpact { get; set; } = 0.30;

		/// <summary>
		/// Минимальная уверенность SL-модели, при которой её сигнал учитывается.
		/// </summary>
		public double SlMinConfidence { get; set; } = 0.55;

		/// <summary>
		/// Порог сильной уверенности SL-модели (для более жёстких корректировок).
		/// </summary>
		public double SlStrongConfidence { get; set; } = 0.70;

		// ===== Кламп top-класса =====

		/// <summary>
		/// Нижняя граница вероятности для доминирующего класса после агрегации.
		/// </summary>
		public double MinTopClassProb { get; set; } = 0.34;

		/// <summary>
		/// Верхняя граница вероятности для доминирующего класса после агрегации.
		/// </summary>
		public double MaxTopClassProb { get; set; } = 0.90;

		/// <summary>
		/// Флаг: можно ли ослаблять влияние SL, если он противоречит сильному дневному сигналу.
		/// Конкретная логика применения будет добавлена позже.
		/// </summary>
		public bool AllowCounterSlAdjustments { get; set; } = false;

		/// <summary>
		/// Имя/версия конфигурации, удобно для логов и сериализации.
		/// </summary>
		public string Name { get; set; } = "default";
		}
	}
