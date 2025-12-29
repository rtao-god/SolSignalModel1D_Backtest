using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Aggregation
{
    public static class AggregationProbsSnapshotBuilder
    {
        public static AggregationProbsSnapshot Build(
            AggregationInputSets sets,
            int recentDays,
            int debugLastDays)
        {
            if (sets == null) throw new ArgumentNullException(nameof(sets));
            if (recentDays <= 0) throw new ArgumentOutOfRangeException(nameof(recentDays), "recentDays must be > 0.");
            if (debugLastDays <= 0) throw new ArgumentOutOfRangeException(nameof(debugLastDays), "debugLastDays must be > 0.");

            var eligible = OrderAndValidateEntryDayKey(sets.Eligible, "eligible");
            var excluded = OrderAndValidateEntryDayKey(sets.Excluded, "excluded");
            var train = OrderAndValidateEntryDayKey(sets.Train, "train");
            var oos = OrderAndValidateEntryDayKey(sets.Oos, "oos");

            EnsureSplitInvariants(sets.Boundary.TrainUntilExitDayKeyUtc, eligible, train, oos);

            int totalInput = eligible.Count + excluded.Count;

            if (totalInput == 0)
            {
                throw new InvalidOperationException("[agg-probs] totalInput=0: невозможно построить вероятности без данных.");
            }

            var (minDateUtc, maxDateUtc) = ComputeMinMaxUtc(eligible, excluded);

            var segments = new List<AggregationProbsSegmentSnapshot>(4);

            AddSegment(
                segments,
                segmentName: "Train",
                segmentLabel: $"Train (exit<= {sets.Boundary.TrainUntilIsoDate})",
                train);

            AddSegment(
                segments,
                segmentName: "OOS",
                segmentLabel: $"OOS (exit>  {sets.Boundary.TrainUntilIsoDate})",
                oos);

            var recent = BuildRecent(eligible, recentDays);
            AddSegment(
                segments,
                segmentName: "Recent",
                segmentLabel: $"Recent({recentDays}d)",
                recent);

            AddSegment(
                segments,
                segmentName: "Full",
                segmentLabel: "Full (eligible days)",
                eligible);

            var debug = BuildDebugLastDays(eligible, debugLastDays);

            return new AggregationProbsSnapshot
            {
                MinDateUtc = minDateUtc,
                MaxDateUtc = maxDateUtc,
                TotalInputRecords = totalInput,
                ExcludedCount = excluded.Count,
                Segments = segments,
                DebugLastDays = debug
            };
        }

        private static List<BacktestAggRow> OrderAndValidateEntryDayKey(IReadOnlyList<BacktestAggRow> rows, string setName)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows), $"[{setName}] set is null.");

            if (rows.Count == 0)
                return new List<BacktestAggRow>(0);

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].EntryDayKeyUtc.Equals(default(EntryDayKeyUtc)))
                    throw new InvalidOperationException($"[agg-probs] BacktestAggRow.EntryDayKeyUtc is default in {setName} set.");
            }

            return rows.OrderBy(r => r.EntryDayKeyUtc.Value).ToList();
        }

        private static void EnsureSplitInvariants(
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            IReadOnlyList<BacktestAggRow> eligible,
            IReadOnlyList<BacktestAggRow> train,
            IReadOnlyList<BacktestAggRow> oos)
        {
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new InvalidOperationException("[agg-probs] trainUntilExitDayKeyUtc is default.");

            if (train.Count + oos.Count != eligible.Count)
            {
                throw new InvalidOperationException(
                    $"[agg-probs] Split invariant violated: train({train.Count}) + oos({oos.Count}) != eligible({eligible.Count}).");
            }

            // ВАЖНО:
            // - BacktestAggRow.EntryDayKeyUtc — entry day-key.
            // - trainUntilExitDayKeyUtc — baseline-exit day-key.
            // Их прямое сравнение запрещено; корректность обеспечивается апстрим-сплитом.
        }

        private static (DateTime Min, DateTime Max) ComputeMinMaxUtc(
            IReadOnlyList<BacktestAggRow> eligible,
            IReadOnlyList<BacktestAggRow> excluded)
        {
            DateTime min = default;
            DateTime max = default;
            bool has = false;

            if (eligible.Count > 0)
            {
                min = eligible[0].EntryDayKeyUtc.Value;
                max = eligible[^1].EntryDayKeyUtc.Value;
                has = true;
            }

            if (excluded.Count > 0)
            {
                var exMin = excluded[0].EntryDayKeyUtc.Value;
                var exMax = excluded[^1].EntryDayKeyUtc.Value;

                if (!has)
                {
                    min = exMin;
                    max = exMax;
                    has = true;
                }
                else
                {
                    if (exMin < min) min = exMin;
                    if (exMax > max) max = exMax;
                }
            }

            if (!has)
                throw new InvalidOperationException("[agg-probs] no dates to compute min/max.");

            return (min, max);
        }

        private static IReadOnlyList<BacktestAggRow> BuildRecent(IReadOnlyList<BacktestAggRow> eligible, int recentDays)
        {
            if (eligible.Count == 0)
                throw new InvalidOperationException("[agg-probs] eligible=0: recent-сегмент не может быть вычислен.");

            var maxDateUtc = eligible[^1].EntryDayKeyUtc.Value;
            var fromRecentUtc = maxDateUtc.AddDays(-recentDays);

            var recent = eligible.Where(r => r.EntryDayKeyUtc.Value >= fromRecentUtc).ToList();
            return recent.Count == 0 ? eligible : recent;
        }

        private static void AddSegment(
            List<AggregationProbsSegmentSnapshot> dst,
            string segmentName,
            string segmentLabel,
            IReadOnlyList<BacktestAggRow> seg)
        {
            if (seg == null) throw new ArgumentNullException(nameof(seg));

            if (seg.Count == 0)
            {
                throw new InvalidOperationException($"[agg-probs] empty segment '{segmentName}'.");
            }

            double sumUpDay = 0, sumFlatDay = 0, sumDownDay = 0;
            double sumUpDm = 0, sumFlatDm = 0, sumDownDm = 0;
            double sumUpTot = 0, sumFlatTot = 0, sumDownTot = 0;

            double sumSumDay = 0, sumSumDm = 0, sumSumTot = 0;
            double sumConfDay = 0, sumConfMicro = 0;

            int slNonZero = 0;

            foreach (var r in seg)
            {
                var d = r.EntryDayKeyUtc.Value;

                ValidateTri(d, "Day", r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day);
                ValidateTri(d, "Day+Micro", r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro);
                ValidateTri(d, "Total", r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total);

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

                if (r.SlProb.HasValue) slNonZero++;
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

            if (day.Sum <= 1e-6 || dm.Sum <= 1e-6 || tot.Sum <= 1e-6)
                throw new InvalidOperationException("[agg-probs] Degenerate probabilities: avg sum ≈ 0.");

            dst.Add(new AggregationProbsSegmentSnapshot
            {
                SegmentName = segmentName,
                SegmentLabel = segmentLabel,
                FromDateUtc = seg[0].EntryDayKeyUtc.Value,
                ToDateUtc = seg[^1].EntryDayKeyUtc.Value,
                RecordsCount = seg.Count,
                Day = day,
                DayMicro = dm,
                Total = tot,
                AvgConfDay = sumConfDay * invN,
                AvgConfMicro = sumConfMicro * invN,
                RecordsWithSlScore = slNonZero
            });
        }

        private static IReadOnlyList<AggregationProbsDebugRow> BuildDebugLastDays(
            IReadOnlyList<BacktestAggRow> eligible,
            int debugLastDays)
        {
            if (eligible.Count == 0)
                throw new InvalidOperationException("[agg-probs] eligible=0: debug-last-days не может быть вычислен.");

            var tail = eligible.Skip(Math.Max(0, eligible.Count - debugLastDays)).ToList();

            const double eps = 1e-3;
            var res = new List<AggregationProbsDebugRow>(tail.Count);

            foreach (var r in tail)
            {
                bool microUsed = HasOverlayChange(
                    r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day,
                    r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro,
                    eps);

                bool slDecision = r.SlHighDecision.HasValue && r.SlHighDecision.Value;
                bool slUsed =
                    HasOverlayChange(
                        r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro,
                        r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total,
                        eps)
                    || slDecision
                    || r.SlProb.HasValue;

                bool microAgree = r.PredLabel_DayMicro == r.PredLabel_Day;

                bool slPenLong = r.ProbUp_Total < r.ProbUp_DayMicro - eps;
                bool slPenShort = r.ProbDown_Total < r.ProbDown_DayMicro - eps;

                res.Add(new AggregationProbsDebugRow
                {
                    DateUtc = r.EntryDayKeyUtc.Value,
                    TrueLabel = r.TrueLabel,
                    PredDay = r.PredLabel_Day,
                    PredDayMicro = r.PredLabel_DayMicro,
                    PredTotal = r.PredLabel_Total,
                    PDay = new TriProb(r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day),
                    PDayMicro = new TriProb(r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro),
                    PTotal = new TriProb(r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total),
                    MicroUsed = microUsed,
                    SlUsed = slUsed,
                    MicroAgree = microAgree,
                    SlPenLong = slPenLong,
                    SlPenShort = slPenShort
                });
            }

            return res;
        }

        private static void ValidateTri(DateTime dateUtc, string layer, double up, double flat, double down)
        {
            if (double.IsNaN(up) || double.IsNaN(flat) || double.IsNaN(down) ||
                double.IsInfinity(up) || double.IsInfinity(flat) || double.IsInfinity(down))
            {
                throw new InvalidOperationException(
                    $"[agg-probs] Non-finite probability in layer '{layer}' for date {dateUtc:O}: up={up}, flat={flat}, down={down}.");
            }

            if (up < 0.0 || flat < 0.0 || down < 0.0)
            {
                throw new InvalidOperationException(
                    $"[agg-probs] Negative probability in layer '{layer}' for date {dateUtc:O}: up={up}, flat={flat}, down={down}.");
            }

            double sum = up + flat + down;
            if (sum <= 0.0)
            {
                throw new InvalidOperationException(
                    $"[agg-probs] Degenerate probability triple (sum<=0) in layer '{layer}' for date {dateUtc:O}: up={up}, flat={flat}, down={down}.");
            }
        }

        private static bool HasOverlayChange(
            double up1, double flat1, double down1,
            double up2, double flat2, double down2,
            double eps)
        {
            return Math.Abs(up1 - up2) > eps
                || Math.Abs(flat1 - flat2) > eps
                || Math.Abs(down1 - down2) > eps;
        }
    }
}
