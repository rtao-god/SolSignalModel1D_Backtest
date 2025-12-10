using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats
	{
	/// <summary>
	/// Центральный билдёр мульти-снимка модельных статистик.
	/// Здесь единообразно режутся PredictionRecord на сегменты:
	/// Train / OOS / Full / Recent.
	/// Вся математика остаётся в BacktestModelStatsSnapshotBuilder.
	/// </summary>
	public static class BacktestModelStatsMultiSnapshotBuilder
		{
		public static BacktestModelStatsMultiSnapshot Build (
			IReadOnlyList<PredictionRecord> allRecords,
			IReadOnlyList<Candle1m> sol1m,
			TimeZoneInfo nyTz,
			double dailyTpPct,
			double dailySlPct,
			DateTime trainUntilUtc,
			int recentDays,
			ModelRunKind runKind )
			{
			if (allRecords == null) throw new ArgumentNullException (nameof (allRecords));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));
			if (recentDays <= 0) throw new ArgumentOutOfRangeException (nameof (recentDays), "recentDays must be > 0.");

			var multi = new BacktestModelStatsMultiSnapshot
				{
				Meta =
					{
					RunKind = runKind,
					TrainUntilUtc = trainUntilUtc,
					RecentDays = recentDays
					}
				};

			if (allRecords.Count == 0)
				{
				// Пустой запуск: только мета, без сегментов.
				multi.Meta.HasOos = false;
				multi.Meta.TrainRecordsCount = 0;
				multi.Meta.OosRecordsCount = 0;
				multi.Meta.TotalRecordsCount = 0;
				multi.Meta.RecentRecordsCount = 0;
				return multi;
				}

			// 1) Стабильно упорядочиваем все записи по дате.
			var ordered = allRecords
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var minDateUtc = ordered.First ().DateUtc;
			var maxDateUtc = ordered.Last ().DateUtc;

			// 2) Сегменты по границе trainUntilUtc.
			var trainRecords = new List<PredictionRecord> ();
			var oosRecords = new List<PredictionRecord> ();

			foreach (var r in ordered)
				{
				// та же логика, что и при обучении (DailyDatasetBuilder.FilterByBaselineExit)
				var exitUtc = Windowing.ComputeBaselineExitUtc (r.DateUtc, nyTz);

				if (exitUtc <= trainUntilUtc)
					trainRecords.Add (r);  // это "train" и для метрик
				else
					oosRecords.Add (r);    // это "OOS" для метрик
				}

			var fullRecords = ordered;

			// 3) Recent-окно относительно максимальной даты.
			var fromRecentUtc = maxDateUtc.AddDays (-recentDays);
			var recentRecords = ordered
				.Where (r => r.DateUtc >= fromRecentUtc)
				.ToList ();

			if (recentRecords.Count == 0)
				{
				// На всякий случай не оставляем recent-пустым:
				// если данных за последние N дней нет, используем всю историю.
				recentRecords = ordered;
				}

			// 4) Заполняем метаданные.
			var meta = multi.Meta;
			meta.HasOos = oosRecords.Count > 0;
			meta.TrainRecordsCount = trainRecords.Count;
			meta.OosRecordsCount = oosRecords.Count;
			meta.TotalRecordsCount = ordered.Count;
			meta.RecentRecordsCount = recentRecords.Count;

			// 5) Собираем сегменты.
			AddSegmentIfNotEmpty (
				multi,
				ModelStatsSegmentKind.OosOnly,
				label: "OOS-only (DateUtc > trainUntil)",
				oosRecords,
				sol1m,
				nyTz,
				dailyTpPct,
				dailySlPct);

			AddSegmentIfNotEmpty (
				multi,
				ModelStatsSegmentKind.TrainOnly,
				label: "Train-only (DateUtc <= trainUntil)",
				trainRecords,
				sol1m,
				nyTz,
				dailyTpPct,
				dailySlPct);

			AddSegmentIfNotEmpty (
				multi,
				ModelStatsSegmentKind.RecentWindow,
				label: $"Recent window (last {recentDays} days)",
				recentRecords,
				sol1m,
				nyTz,
				dailyTpPct,
				dailySlPct);

			AddSegmentIfNotEmpty (
				multi,
				ModelStatsSegmentKind.FullHistory,
				label: "Full history",
				fullRecords,
				sol1m,
				nyTz,
				dailyTpPct,
				dailySlPct);

			return multi;
			}

		/// <summary>
		/// Внутренний хелпер: если список записей непустой, строит BacktestModelStatsSnapshot
		/// и добавляет его как сегмент в мульти-снимок.
		/// </summary>
		private static void AddSegmentIfNotEmpty (
			BacktestModelStatsMultiSnapshot multi,
			ModelStatsSegmentKind kind,
			string label,
			IReadOnlyList<PredictionRecord> segmentRecords,
			IReadOnlyList<Candle1m> sol1m,
			TimeZoneInfo nyTz,
			double dailyTpPct,
			double dailySlPct )
			{
			if (segmentRecords == null) throw new ArgumentNullException (nameof (segmentRecords));
			if (segmentRecords.Count == 0)
				return;

			var stats = BacktestModelStatsSnapshotBuilder.Compute (
				records: segmentRecords,
				sol1m: sol1m,
				dailyTpPct: dailyTpPct,
				dailySlPct: dailySlPct,
				nyTz: nyTz);

			var segment = new BacktestModelStatsSegmentSnapshot
				{
				Kind = kind,
				Label = label,
				FromDateUtc = stats.FromDateUtc,
				ToDateUtc = stats.ToDateUtc,
				RecordsCount = segmentRecords.Count,
				Stats = stats
				};

			multi.Segments.Add (segment);
			}
		}
	}
