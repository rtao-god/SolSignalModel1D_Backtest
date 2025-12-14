using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation
	{
	/// <summary>
	/// Билдёр snapshot-а вероятностей по сегментам.
	/// Важно: никаких Console.WriteLine — только расчёты.
	/// </summary>
	public static class AggregationProbsSnapshotBuilder
		{
		public static AggregationProbsSnapshot Build (
			IReadOnlyList<BacktestAggRow> rows,
			TrainBoundary boundary,
			int recentDays,
			int debugLastDays )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			// ВАЖНО: boundary может быть struct в твоём коде.
			// Проверку на null делаем только если это reference-type.
			// Чтобы сделать "идеально" и без двусмысленности — пришли файл TrainBoundary.cs (см. список ниже).
			if (recentDays <= 0) throw new ArgumentOutOfRangeException (nameof (recentDays), "recentDays must be > 0.");
			if (debugLastDays <= 0) throw new ArgumentOutOfRangeException (nameof (debugLastDays), "debugLastDays must be > 0.");

			if (rows.Count == 0)
				{
				return new AggregationProbsSnapshot
					{
					MinDateUtc = default,
					MaxDateUtc = default,
					TotalInputRecords = 0,
					ExcludedCount = 0,
					Segments = Array.Empty<AggregationProbsSegmentSnapshot> (),
					DebugLastDays = Array.Empty<AggregationProbsDebugRow> ()
					};
				}

			var ordered = rows
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var minDateUtc = ordered[0].DateUtc;
			var maxDateUtc = ordered[^1].DateUtc;

			var split = boundary.Split (ordered, r => r.DateUtc);
			var train = split.Train;
			var oos = split.Oos;

			// Инвариант: excluded никогда не участвуют в метриках сегментов.
			var eligible = new List<BacktestAggRow> (train.Count + oos.Count);
			eligible.AddRange (train);
			eligible.AddRange (oos);

			// На всякий случай фиксируем порядок, чтобы Recent/Debug были детерминированны.
			eligible = eligible.OrderBy (r => r.DateUtc).ToList ();

			var segments = new List<AggregationProbsSegmentSnapshot> (4);

			AddSegment (
				segments,
				segmentName: "Train",
				segmentLabel: $"Train (exit<= {boundary.TrainUntilIsoDate})",
				train);

			AddSegment (
				segments,
				segmentName: "OOS",
				segmentLabel: $"OOS (exit>  {boundary.TrainUntilIsoDate})",
				oos);

			var recent = BuildRecent (eligible, recentDays);
			AddSegment (
				segments,
				segmentName: "Recent",
				segmentLabel: $"Recent({recentDays}d)",
				recent);

			AddSegment (
				segments,
				segmentName: "Full",
				segmentLabel: "Full (eligible days)",
				eligible);

			var debug = BuildDebugLastDays (eligible, debugLastDays);

			return new AggregationProbsSnapshot
				{
				MinDateUtc = minDateUtc,
				MaxDateUtc = maxDateUtc,
				TotalInputRecords = ordered.Count,
				ExcludedCount = split.Excluded.Count,
				Segments = segments,
				DebugLastDays = debug
				};
			}

		private static IReadOnlyList<BacktestAggRow> BuildRecent ( IReadOnlyList<BacktestAggRow> eligible, int recentDays )
			{
			if (eligible.Count == 0)
				return Array.Empty<BacktestAggRow> ();

			var maxDateUtc = eligible[^1].DateUtc;
			var fromRecentUtc = maxDateUtc.AddDays (-recentDays);

			var recent = eligible
				.Where (r => r.DateUtc >= fromRecentUtc)
				.ToList ();

			// Если по каким-то причинам “recentDays” вырезал всё, возвращаем eligible,
			// чтобы не печатать пустой сегмент, который вводит в заблуждение.
			return recent.Count == 0 ? eligible : recent;
			}

		private static void AddSegment (
			List<AggregationProbsSegmentSnapshot> dst,
			string segmentName,
			string segmentLabel,
			IReadOnlyList<BacktestAggRow> seg )
			{
			if (seg == null) throw new ArgumentNullException (nameof (seg));

			if (seg.Count == 0)
				{
				dst.Add (new AggregationProbsSegmentSnapshot
					{
					SegmentName = segmentName,
					SegmentLabel = segmentLabel,
					FromDateUtc = null,
					ToDateUtc = null,
					RecordsCount = 0,
					Day = new AggregationLayerAvg { PUp = 0, PFlat = 0, PDown = 0, Sum = 0 },
					DayMicro = new AggregationLayerAvg { PUp = 0, PFlat = 0, PDown = 0, Sum = 0 },
					Total = new AggregationLayerAvg { PUp = 0, PFlat = 0, PDown = 0, Sum = 0 },
					AvgConfDay = 0,
					AvgConfMicro = 0,
					RecordsWithSlScore = 0
					});
				return;
				}

			double sumUpDay = 0, sumFlatDay = 0, sumDownDay = 0;
			double sumUpDm = 0, sumFlatDm = 0, sumDownDm = 0;
			double sumUpTot = 0, sumFlatTot = 0, sumDownTot = 0;

			double sumSumDay = 0, sumSumDm = 0, sumSumTot = 0;
			double sumConfDay = 0, sumConfMicro = 0;

			int slNonZero = 0;

			foreach (var r in seg)
				{
				ValidateTri (r.DateUtc, "Day", r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day);
				ValidateTri (r.DateUtc, "Day+Micro", r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro);
				ValidateTri (r.DateUtc, "Total", r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total);

				sumUpDay += r.ProbUp_Day;
				sumFlatDay += r.ProbFlat_Day;
				sumDownDay += r.ProbDown_Day;

				sumUpDm += r.ProbUp_DayMicro;
				sumFlatDm += r.ProbFlat_DayMicro;
				sumDownDm += r.ProbDown_DayMicro;

				sumUpTot += r.ProbUp_Total;
				sumFlatTot += r.ProbFlat_Total;
				sumDownTot += r.ProbDown_Total;

				sumSumDay += r.ProbUp_Day + r.ProbFlat_Day + r.ProbDown_Day;
				sumSumDm += r.ProbUp_DayMicro + r.ProbFlat_DayMicro + r.ProbDown_DayMicro;
				sumSumTot += r.ProbUp_Total + r.ProbFlat_Total + r.ProbDown_Total;

				sumConfDay += r.Conf_Day;
				sumConfMicro += r.Conf_Micro;

				if (r.SlProb > 0.0) slNonZero++;
				}

			double invN = 1.0 / seg.Count;

			var day = new AggregationLayerAvg
				{
				PUp = sumUpDay * invN,
				PFlat = sumFlatDay * invN,
				PDown = sumDownDay * invN,
				Sum = sumSumDay * invN
				};

			var dm = new AggregationLayerAvg
				{
				PUp = sumUpDm * invN,
				PFlat = sumFlatDm * invN,
				PDown = sumDownDm * invN,
				Sum = sumSumDm * invN
				};

			var tot = new AggregationLayerAvg
				{
				PUp = sumUpTot * invN,
				PFlat = sumFlatTot * invN,
				PDown = sumDownTot * invN,
				Sum = sumSumTot * invN
				};

			// Защита от деградации пайплайна: вероятности должны быть осмысленными,
			// иначе печать "красиво" замаскирует проблему в апстриме.
			if (day.Sum <= 1e-6 || dm.Sum <= 1e-6 || tot.Sum <= 1e-6)
				throw new InvalidOperationException ("[agg-probs] Degenerate probabilities: avg sum ≈ 0.");

			dst.Add (new AggregationProbsSegmentSnapshot
				{
				SegmentName = segmentName,
				SegmentLabel = segmentLabel,
				FromDateUtc = seg[0].DateUtc,
				ToDateUtc = seg[^1].DateUtc,
				RecordsCount = seg.Count,
				Day = day,
				DayMicro = dm,
				Total = tot,
				AvgConfDay = sumConfDay * invN,
				AvgConfMicro = sumConfMicro * invN,
				RecordsWithSlScore = slNonZero
				});
			}

		private static IReadOnlyList<AggregationProbsDebugRow> BuildDebugLastDays (
			IReadOnlyList<BacktestAggRow> eligible,
			int debugLastDays )
			{
			if (eligible.Count == 0)
				return Array.Empty<AggregationProbsDebugRow> ();

			var tail = eligible
				.Skip (Math.Max (0, eligible.Count - debugLastDays))
				.ToList ();

			const double eps = 1e-3;
			var res = new List<AggregationProbsDebugRow> (tail.Count);

			foreach (var r in tail)
				{
				bool microUsed = HasOverlayChange (
					r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day,
					r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro,
					eps);

				bool slUsed =
					HasOverlayChange (
						r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro,
						r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total,
						eps)
					|| r.SlHighDecision
					|| r.SlProb > 0.0;

				bool microAgree = r.PredLabel_DayMicro == r.PredLabel_Day;

				bool slPenLong = r.ProbUp_Total < r.ProbUp_DayMicro - eps;
				bool slPenShort = r.ProbDown_Total < r.ProbDown_DayMicro - eps;

				res.Add (new AggregationProbsDebugRow
					{
					DateUtc = r.DateUtc,
					TrueLabel = r.TrueLabel,
					PredDay = r.PredLabel_Day,
					PredDayMicro = r.PredLabel_DayMicro,
					PredTotal = r.PredLabel_Total,
					PDay = new TriProb (r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day),
					PDayMicro = new TriProb (r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro),
					PTotal = new TriProb (r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total),
					MicroUsed = microUsed,
					SlUsed = slUsed,
					MicroAgree = microAgree,
					SlPenLong = slPenLong,
					SlPenShort = slPenShort
					});
				}

			return res;
			}

		private static void ValidateTri ( DateTime dateUtc, string layer, double up, double flat, double down )
			{
			if (double.IsNaN (up) || double.IsNaN (flat) || double.IsNaN (down) ||
				double.IsInfinity (up) || double.IsInfinity (flat) || double.IsInfinity (down))
				{
				throw new InvalidOperationException (
					$"[agg-probs] Non-finite probability in layer '{layer}' for date {dateUtc:O}: up={up}, flat={flat}, down={down}.");
				}

			if (up < 0.0 || flat < 0.0 || down < 0.0)
				{
				throw new InvalidOperationException (
					$"[agg-probs] Negative probability in layer '{layer}' for date {dateUtc:O}: up={up}, flat={flat}, down={down}.");
				}

			double sum = up + flat + down;
			if (sum <= 0.0)
				{
				throw new InvalidOperationException (
					$"[agg-probs] Degenerate probability triple (sum<=0) in layer '{layer}' for date {dateUtc:O}: up={up}, flat={flat}, down={down}.");
				}
			}

		private static bool HasOverlayChange (
			double up1, double flat1, double down1,
			double up2, double flat2, double down2,
			double eps )
			{
			return Math.Abs (up1 - up2) > eps
				|| Math.Abs (flat1 - flat2) > eps
				|| Math.Abs (down1 - down2) > eps;
			}
		}
	}
