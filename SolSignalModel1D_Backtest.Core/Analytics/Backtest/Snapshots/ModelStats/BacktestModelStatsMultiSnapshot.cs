using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats
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
		public ModelStatsSegmentKind Kind { get; set; }

		/// <summary>
		/// Читабельная подпись сегмента, которую можно показывать в консоли/отчётах/фронте.
		/// </summary>
		public string Label { get; set; } = string.Empty;

		/// <summary>
		/// Минимальная дата в сегменте (UTC) по PredictionRecord.DateUtc.
		/// </summary>
		public DateTime FromDateUtc { get; set; }

		/// <summary>
		/// Максимальная дата в сегменте (UTC) по PredictionRecord.DateUtc.
		/// </summary>
		public DateTime ToDateUtc { get; set; }

		/// <summary>
		/// Количество PredictionRecord в сегменте.
		/// </summary>
		public int RecordsCount { get; set; }

		/// <summary>
		/// Подробный снимок модельных статистик по данному сегменту.
		/// </summary>
		public BacktestModelStatsSnapshot Stats { get; set; } = new BacktestModelStatsSnapshot ();
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
		public ModelStatsMeta Meta { get; set; } = new ModelStatsMeta ();

		/// <summary>
		/// Набор сегментов (Train, OOS, Full, Recent).
		/// Список неплотный: пустые сегменты просто не добавляются.
		/// </summary>
		public List<BacktestModelStatsSegmentSnapshot> Segments { get; } =
			new List<BacktestModelStatsSegmentSnapshot> ();
		}
	}
