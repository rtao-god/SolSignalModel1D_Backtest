using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.ModelStats;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats
	{
	/// <summary>
	/// Снимок статистик по одному сегменту (Train/OOS/Full/Recent).
	/// Оборачивает обычный BacktestModelStatsSnapshot и добавляет метаданные сегмента.
	/// </summary>
	public sealed class BacktestModelStatsSegmentSnapshot
		{
		/// <summary>
		/// Тип сегмента: train / oos / full / recent.
		/// </summary>
		public required ModelStatsSegmentKind Kind { get; init; }

		/// <summary>
		/// Читабельная подпись сегмента, которую можно показывать в консоли/отчётах/фронте.
		/// </summary>
		public required string Label { get; init; }

		/// <summary>
		/// Минимальная дата в сегменте (UTC) по PredictionRecord.DateUtc.
		/// </summary>
		public required DateTime FromDateUtc { get; init; }

		/// <summary>
		/// Максимальная дата в сегменте (UTC) по PredictionRecord.DateUtc.
		/// </summary>
		public required DateTime ToDateUtc { get; init; }

		/// <summary>
		/// Количество PredictionRecord в сегменте.
		/// </summary>
		public required int RecordsCount { get; init; }

		/// <summary>
		/// Подробный снимок модельных статистик по данному сегменту.
		/// </summary>
		public required BacktestModelStatsSnapshot Stats { get; init; }
		}

	/// <summary>
	/// Мульти-снимок для бэктестовых модельных статистик.
	/// Содержит:
	/// - общие метаданные запуска (train/OOS/full/recent);
	/// - набор сегментов с независимыми BacktestModelStatsSnapshot.
	/// Этот объект — единственная точка входа для принтеров, отчётов и API.
	/// </summary>
	public sealed class BacktestModelStatsMultiSnapshot
		{
		/// <summary>
		/// Общие метаданные запуска модели и разбиения на сегменты.
		/// </summary>
		public required ModelStatsMeta Meta { get; init; }

		/// <summary>
		/// Набор сегментов (Train, OOS, Full, Recent).
		/// Список неплотный: пустые сегменты просто не добавляются.
		/// </summary>
		public required IReadOnlyList<BacktestModelStatsSegmentSnapshot> Segments { get; init; }
		}
	}
