namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats
	{
	public enum ModelStatsSegmentKind
		{
		FullHistory,   // всё, что есть в records
		TrainOnly,     // DateUtc <= trainUntilUtc
		OosOnly,       // DateUtc >  trainUntilUtc
		RecentWindow   // последние N дней
		}

	public enum ModelRunKind
		{
		Production,    // боевой режим
		Analytics      // чисто аналитический/офлайн прогон
		}

	/// <summary>
	/// Метаданные по запуску модельных статистик и разбиению на сегменты.
	/// Хранятся один раз для всего мульти-снапшота.
	/// </summary>
	public sealed class ModelStatsMeta
		{
		/// <summary>
		/// Режим запуска модели: боевой или аналитический офлайн-прогон.
		/// </summary>
		public ModelRunKind RunKind { get; set; }

		/// <summary>
		/// Есть ли хоть один день после границы trainUntilUtc.
		/// </summary>
		public bool HasOos { get; set; }

		/// <summary>
		/// Граница train: все дни с DateUtc &lt;= TrainUntilUtc считаются train-сегментом.
		/// Может быть null, если граница неизвестна.
		/// </summary>
		public DateTime? TrainUntilUtc { get; set; }

		/// <summary>
		/// Количество записей в train-сегменте (DateUtc &lt;= TrainUntilUtc).
		/// </summary>
		public int TrainRecordsCount { get; set; }

		/// <summary>
		/// Количество записей в OOS-сегменте (DateUtc &gt; TrainUntilUtc).
		/// </summary>
		public int OosRecordsCount { get; set; }

		/// <summary>
		/// Общее количество записей, попавших в Build(...).
		/// </summary>
		public int TotalRecordsCount { get; set; }

		/// <summary>
		/// Размер recent-окна в календарных днях.
		/// </summary>
		public int RecentDays { get; set; }

		/// <summary>
		/// Количество записей в recent-окне.
		/// </summary>
		public int RecentRecordsCount { get; set; }
		}
	}
