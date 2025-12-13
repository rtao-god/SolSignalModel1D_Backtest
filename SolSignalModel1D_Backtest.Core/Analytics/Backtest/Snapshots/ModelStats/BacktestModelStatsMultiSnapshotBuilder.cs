using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats
	{
	public static class BacktestModelStatsMultiSnapshotBuilder
		{
		public static BacktestModelStatsMultiSnapshot Build (
			IReadOnlyList<BacktestRecord> allRecords,
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
				multi.Meta.HasOos = false;
				multi.Meta.TrainRecordsCount = 0;
				multi.Meta.OosRecordsCount = 0;
				multi.Meta.TotalRecordsCount = 0;
				multi.Meta.RecentRecordsCount = 0;
				return multi;
				}

			var ordered = allRecords
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			// ЕДИНЫЙ контракт сплита train/OOS — baseline-exit.
			var boundary = new TrainBoundary (trainUntilUtc, nyTz);
			var split = boundary.Split (ordered, r => r.Causal.DateUtc);

			var trainRecords = split.Train;
			var oosRecords = split.Oos;

			if (split.Excluded.Count > 0)
				{
				// Инвариант: на вход метрик должны приходить только eligible дни.
				// Excluded (обычно weekend) означает, что выше по пайплайну неправильно сформирован список allRecords.
				throw new InvalidOperationException (
					$"[model-stats] Found excluded records (baseline-exit undefined). " +
					$"ExcludedCount={split.Excluded.Count}. " +
					$"This is a pipeline bug: filter out excluded days before analytics.");
				}

			// Full — только “eligible” записи (train+oos), excluded не смешиваем.
			var fullRecords = new List<BacktestRecord> (trainRecords.Count + oosRecords.Count);
			fullRecords.AddRange (trainRecords);
			fullRecords.AddRange (oosRecords);

			var maxDateUtc = fullRecords[^1].Causal.DateUtc;

			// Recent — также только eligible.
			var fromRecentUtc = maxDateUtc.AddDays (-recentDays);
			var recentRecords = fullRecords
				.Where (r => r.Causal.DateUtc >= fromRecentUtc)
				.ToList ();

			if (recentRecords.Count == 0)
				{
				recentRecords = fullRecords;
				}

			var meta = multi.Meta;
			meta.HasOos = oosRecords.Count > 0;
			meta.TrainRecordsCount = trainRecords.Count;
			meta.OosRecordsCount = oosRecords.Count;
			meta.TotalRecordsCount = fullRecords.Count;
			meta.RecentRecordsCount = recentRecords.Count;

			AddSegmentIfNotEmpty (
				multi,
				ModelStatsSegmentKind.OosOnly,
				label: "OOS-only (baseline-exit > trainUntil)",
				oosRecords,
				sol1m,
				nyTz,
				dailyTpPct,
				dailySlPct);

			AddSegmentIfNotEmpty (
				multi,
				ModelStatsSegmentKind.TrainOnly,
				label: "Train-only (baseline-exit <= trainUntil)",
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
				label: "Full history (eligible days)",
				fullRecords,
				sol1m,
				nyTz,
				dailyTpPct,
				dailySlPct);

			return multi;
			}

		private static void AddSegmentIfNotEmpty (
			BacktestModelStatsMultiSnapshot multi,
			ModelStatsSegmentKind kind,
			string label,
			IReadOnlyList<BacktestRecord> segmentRecords,
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
