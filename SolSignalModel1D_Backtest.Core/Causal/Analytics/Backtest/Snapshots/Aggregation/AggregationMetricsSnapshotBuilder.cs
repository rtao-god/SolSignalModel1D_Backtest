using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation
{
    public static class AggregationMetricsSnapshotBuilder
    {
        public static AggregationMetricsSnapshot Build(
            AggregationInputSets sets,
            int recentDays)
        {
            if (sets == null) throw new ArgumentNullException(nameof(sets));
            if (recentDays <= 0) throw new ArgumentOutOfRangeException(nameof(recentDays), "recentDays must be > 0.");

            var eligible = OrderAndValidateDayKey(sets.Eligible, "eligible");
            var excluded = OrderAndValidateDayKey(sets.Excluded, "excluded");
            var train = OrderAndValidateDayKey(sets.Train, "train");
            var oos = OrderAndValidateDayKey(sets.Oos, "oos");

            EnsureSplitInvariants(sets.Boundary.TrainUntilExitDayKeyUtc, eligible, train, oos);

            int totalInput = eligible.Count + excluded.Count;

            if (totalInput == 0)
            {
                return new AggregationMetricsSnapshot
                {
                    TotalInputRecords = 0,
                    ExcludedCount = 0,
                    Segments = Array.Empty<AggregationMetricsSegmentSnapshot>()
                };
            }

            var recent = BuildRecent(eligible, recentDays);

            var segments = new List<AggregationMetricsSegmentSnapshot>(4)
            {
                BuildSegment("Train", $"Train (day<= {sets.Boundary.TrainUntilIsoDate})", train),
                BuildSegment("OOS",  $"OOS (day>  {sets.Boundary.TrainUntilIsoDate})", oos),
                BuildSegment("Recent", $"Recent({recentDays}d)", recent),
                BuildSegment("Full", "Full (eligible days)", eligible)
            };

            return new AggregationMetricsSnapshot
            {
                TotalInputRecords = totalInput,
                ExcludedCount = excluded.Count,
                Segments = segments
            };
        }

        private static List<BacktestAggRow> OrderAndValidateDayKey(IReadOnlyList<BacktestAggRow> rows, string setName)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows), $"[{setName}] set is null.");

            if (rows.Count == 0)
                return new List<BacktestAggRow>(0);

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].DayUtc.Equals(default(DayKeyUtc)))
                    throw new InvalidOperationException($"[agg-metrics] BacktestAggRow.DayUtc is default in {setName} set.");
            }

            return rows.OrderBy(r => r.DayUtc.Value).ToList();
        }

        private static void EnsureSplitInvariants(
            DayKeyUtc trainUntilExitDayKeyUtc,
            IReadOnlyList<BacktestAggRow> eligible,
            IReadOnlyList<BacktestAggRow> train,
            IReadOnlyList<BacktestAggRow> oos)
        {
            if (trainUntilExitDayKeyUtc.Equals(default(DayKeyUtc)))
                throw new InvalidOperationException("[agg-metrics] trainUntilExitDayKeyUtc is default.");

            if (train.Count + oos.Count != eligible.Count)
            {
                throw new InvalidOperationException(
                    $"[agg-metrics] Split invariant violated: train({train.Count}) + oos({oos.Count}) != eligible({eligible.Count}).");
            }

            var cut = trainUntilExitDayKeyUtc.Value;

            for (int i = 0; i < train.Count; i++)
            {
                if (train[i].DayUtc.Value > cut)
                    throw new InvalidOperationException($"[agg-metrics] Train set contains day > trainUntil: {train[i].DayUtc.Value:O} > {cut:O}.");
            }

            for (int i = 0; i < oos.Count; i++)
            {
                if (oos[i].DayUtc.Value <= cut)
                    throw new InvalidOperationException($"[agg-metrics] OOS set contains day <= trainUntil: {oos[i].DayUtc.Value:O} <= {cut:O}.");
            }
        }

        private static IReadOnlyList<BacktestAggRow> BuildRecent(IReadOnlyList<BacktestAggRow> eligible, int recentDays)
        {
            if (eligible.Count == 0)
                return Array.Empty<BacktestAggRow>();

            var maxDay = eligible[^1].DayUtc.Value;
            var from = maxDay.AddDays(-recentDays);

            var recent = eligible.Where(r => r.DayUtc.Value >= from).ToList();
            return recent.Count == 0 ? eligible : recent;
        }

        private static AggregationMetricsSegmentSnapshot BuildSegment(
            string name,
            string label,
            IReadOnlyList<BacktestAggRow> seg)
        {
            if (seg == null) throw new ArgumentNullException(nameof(seg));

            if (seg.Count == 0)
            {
                return new AggregationMetricsSegmentSnapshot
                {
                    SegmentName = name,
                    SegmentLabel = label,
                    FromDateUtc = null,
                    ToDateUtc = null,
                    RecordsCount = 0,
                    Day = EmptyLayer("Day"),
                    DayMicro = EmptyLayer("Day+Micro"),
                    Total = EmptyLayer("Total")
                };
            }

            return new AggregationMetricsSegmentSnapshot
            {
                SegmentName = name,
                SegmentLabel = label,
                FromDateUtc = seg[0].DayUtc.Value,
                ToDateUtc = seg[^1].DayUtc.Value,
                RecordsCount = seg.Count,

                Day = ComputeLayerMetrics(seg, "Day", r => r.PredLabel_Day, r => (r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day)),
                DayMicro = ComputeLayerMetrics(seg, "Day+Micro", r => r.PredLabel_DayMicro, r => (r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro)),
                Total = ComputeLayerMetrics(seg, "Total", r => r.PredLabel_Total, r => (r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total))
            };
        }

        private static LayerMetricsSnapshot EmptyLayer(string layerName)
        {
            return new LayerMetricsSnapshot
            {
                LayerName = layerName,
                Confusion = new int[3, 3],
                N = 0,
                Correct = 0,
                Accuracy = double.NaN,
                MicroF1 = double.NaN,
                LogLoss = double.NaN,
                InvalidForLogLoss = 0,
                ValidForLogLoss = 0
            };
        }

        private static LayerMetricsSnapshot ComputeLayerMetrics(
            IReadOnlyList<BacktestAggRow> seg,
            string layerName,
            Func<BacktestAggRow, int> predSelector,
            Func<BacktestAggRow, (double up, double flat, double down)> probSelector)
        {
            var conf = new int[3, 3];
            int n = seg.Count;
            int correct = 0;
            double sumLog = 0.0;

            int invalidForLogLoss = 0;
            int validForLogLoss = 0;

            foreach (var r in seg)
            {
                int y = r.TrueLabel;
                if (y < 0 || y > 2)
                    throw new InvalidOperationException($"[agg-metrics] Unexpected TrueLabel={y} for day {r.DayUtc.Value:O}. Expected 0/1/2.");

                int pred = predSelector(r);
                if (pred < 0 || pred > 2)
                    throw new InvalidOperationException($"[agg-metrics] Unexpected predicted label={pred} in layer '{layerName}' for day {r.DayUtc.Value:O}. Expected 0/1/2.");

                var (pUp, pFlat, pDown) = probSelector(r);

                if (double.IsNaN(pUp) || double.IsNaN(pFlat) || double.IsNaN(pDown) ||
                    double.IsInfinity(pUp) || double.IsInfinity(pFlat) || double.IsInfinity(pDown))
                    throw new InvalidOperationException($"[agg-metrics] Non-finite probability in layer '{layerName}' for day {r.DayUtc.Value:O}. P_up={pUp}, P_flat={pFlat}, P_down={pDown}.");

                if (pUp < 0.0 || pFlat < 0.0 || pDown < 0.0)
                    throw new InvalidOperationException($"[agg-metrics] Negative probability in layer '{layerName}' for day {r.DayUtc.Value:O}. P_up={pUp}, P_flat={pFlat}, P_down={pDown}.");

                double sum = pUp + pFlat + pDown;
                if (sum <= 0.0)
                    throw new InvalidOperationException($"[agg-metrics] Degenerate probability triple (sum<=0) in layer '{layerName}' for day {r.DayUtc.Value:O}. P_up={pUp}, P_flat={pFlat}, P_down={pDown}.");

                double pTrue = y switch
                {
                    2 => pUp,
                    1 => pFlat,
                    0 => pDown,
                    _ => throw new InvalidOperationException("Unreachable label branch")
                };

                conf[y, pred]++;

                if (pred == y)
                    correct++;

                if (pTrue <= 0.0) invalidForLogLoss++;
                else
                {
                    validForLogLoss++;
                    sumLog += Math.Log(pTrue);
                }
            }

            double accuracy = n > 0 ? (double)correct / n : double.NaN;
            double microF1 = accuracy;

            double logLoss = validForLogLoss == 0 ? double.NaN : -sumLog / validForLogLoss;

            return new LayerMetricsSnapshot
            {
                LayerName = layerName,
                Confusion = conf,
                N = n,
                Correct = correct,
                Accuracy = accuracy,
                MicroF1 = microF1,
                LogLoss = logLoss,
                InvalidForLogLoss = invalidForLogLoss,
                ValidForLogLoss = validForLogLoss
            };
        }
    }
}
