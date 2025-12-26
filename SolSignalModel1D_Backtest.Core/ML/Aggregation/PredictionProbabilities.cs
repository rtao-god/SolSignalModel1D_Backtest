namespace SolSignalModel1D_Backtest.Core.ML.Aggregation
	{
	/// <summary>
	/// Сводный DTO, который держит все уровни вероятностей по одному дню:
	/// базовый дневной слой, день+микро, день+микро+SL и исходные слои.
	/// </summary>
	public struct PredictionProbabilities
		{
		/// <summary>
		/// Базовые дневные вероятности от основной модели (без микро и SL).
		/// </summary>
		public DailyProbabilities Day { get; set; }

		/// <summary>
		/// Дневные вероятности после учёта микро-слоя (день+микро).
		/// </summary>
		public DailyProbabilities DayWithMicro { get; set; }

		/// <summary>
		/// Финальные вероятности после учёта микро и SL (день+микро+SL).
		/// </summary>
		public DailyProbabilities Total { get; set; }

		/// <summary>
		/// Исходный прогноз микро-слоя, использованный при агрегации.
		/// </summary>
		public MicroProbabilities MicroLayer { get; set; }

		/// <summary>
		/// Исходный SL-прогноз, использованный при агрегации.
		/// </summary>
		public SlProbabilities SlLayer { get; set; }

		/// <summary>
		/// Режим/стратегия агрегации, по которой были получены DayWithMicro/Total
		/// (например, "baseline-v1", "micro-soft", "micro-hard+sl-cut").
		/// </summary>
		public string AggregationMode { get; set; }

		/// <summary>
		/// Дополнительные отладочные заметки по конкретному дню и агрегации.
		/// Можно использовать для трассировки формул в логах.
		/// </summary>
		public string? DebugNotes { get; set; }
		}
	}
