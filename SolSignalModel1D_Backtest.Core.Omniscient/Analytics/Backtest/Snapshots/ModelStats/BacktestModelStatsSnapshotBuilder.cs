using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Snapshots.ModelStats
{
    /// <summary>
    /// Билдёр снимка модельных статистик.
    /// </summary>
    public static class BacktestModelStatsSnapshotBuilder
    {
        private static EntryUtc EntryUtcOf(BacktestRecord r) => new EntryUtc(r.Causal.EntryUtc.Value);

        private sealed class SlThresholdDay
        {
            public bool IsSlDay { get; set; }
            public double Prob { get; set; }
        }

        private enum DayOutcome
        {
            None = 0,
            TpFirst = 1,
            SlFirst = 2
        }

        public static BacktestModelStatsSnapshot Compute(
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (sol1m == null) throw new ArgumentNullException(nameof(sol1m));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var snapshot = new BacktestModelStatsSnapshot();

            if (records.Count > 0)
            {
                snapshot.FromDateUtc = records.Min(r => r.EntryDayKeyUtc.Value);
                snapshot.ToDateUtc = records.Max(r => r.EntryDayKeyUtc.Value);
            }

            ComputeDailyConfusion(records, snapshot.Daily);
            ComputeTrendDirectionConfusion(records, snapshot.Trend);
            ComputeSlStats(records, sol1m, dailyTpPct, dailySlPct, nyTz, snapshot.Sl);

            return snapshot;
        }

        private static void ComputeDailyConfusion(IReadOnlyList<BacktestRecord> records, DailyConfusionStats daily)
        {
            int[,] m = new int[3, 3];
            int[] rowSum = new int[3];
            int total = 0;

            foreach (var r in records)
            {
                if (r.TrueLabel is < 0 or > 2) continue;
                if (r.PredLabel is < 0 or > 2) continue;

                m[r.TrueLabel, r.PredLabel]++;
                rowSum[r.TrueLabel]++;
                total++;
            }

            int diag = 0;

            for (int y = 0; y < 3; y++)
            {
                int correct = m[y, y];
                int totalRow = rowSum[y];
                double acc = totalRow > 0 ? (double)correct / totalRow * 100.0 : 0.0;

                diag += correct;

                daily.Rows.Add(new DailyClassStatsRow
                {
                    TrueLabel = y,
                    LabelName = LabelName(y),
                    Pred0 = m[y, 0],
                    Pred1 = m[y, 1],
                    Pred2 = m[y, 2],
                    Correct = correct,
                    Total = totalRow,
                    AccuracyPct = acc
                });
            }

            daily.OverallCorrect = diag;
            daily.OverallTotal = total;
            daily.OverallAccuracyPct = total > 0 ? (double)diag / total * 100.0 : 0.0;
        }

        private static string LabelName(int x) => x switch
        {
            0 => "0 (down)",
            1 => "1 (flat)",
            2 => "2 (up)",
            _ => x.ToString()
        };

        private static void ComputeTrendDirectionConfusion(IReadOnlyList<BacktestRecord> records, TrendDirectionStats trend)
        {
            int[,] m = new int[2, 2];
            int[] rowSum = new int[2];
            int total = 0;

            foreach (var r in records)
            {
                if (r.TrueLabel is < 0 or > 2) continue;

                int? trueDir = r.TrueLabel switch
                {
                    0 => 0,
                    2 => 1,
                    _ => null
                };

                if (trueDir is null)
                    continue;

                if (!TryGetPredDirection(r, out var predUp, out var predDown))
                    continue;

                int? predDir = null;
                if (predUp && !predDown) predDir = 1;
                else if (predDown && !predUp) predDir = 0;

                if (predDir is null)
                    continue;

                m[trueDir.Value, predDir.Value]++;
                rowSum[trueDir.Value]++;
                total++;
            }

            string[] names = { "DOWN days", "UP days" };

            int diag = 0;

            for (int y = 0; y < 2; y++)
            {
                int correct = m[y, y];
                int totalRow = rowSum[y];
                double acc = totalRow > 0 ? (double)correct / totalRow * 100.0 : 0.0;

                diag += correct;

                trend.Rows.Add(new TrendDirectionStatsRow
                {
                    Name = names[y],
                    TrueIndex = y,
                    PredDown = m[y, 0],
                    PredUp = m[y, 1],
                    Correct = correct,
                    Total = totalRow,
                    AccuracyPct = acc
                });
            }

            trend.OverallCorrect = diag;
            trend.OverallTotal = total;
            trend.OverallAccuracyPct = total > 0 ? (double)diag / total * 100.0 : 0.0;
        }

        private static void ComputeSlStats(
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz,
            SlStats slStats)
        {
            if (slStats == null) throw new ArgumentNullException(nameof(slStats));

            var minutes = sol1m.OrderBy(m => m.OpenTimeUtc).ToList();

            int tp_low = 0, tp_high = 0, sl_low = 0, sl_high = 0;
            int slSaved = 0;

            int totalSignalDays = 0;
            int scoredDays = 0;

            var prPoints = new List<(double Score, int Label)>();
            var thrDays = new List<SlThresholdDay>();

            foreach (var r in records)
            {
                if (!TryGetPredDirection(r, out var goLong, out var goShort))
                    continue;

                totalSignalDays++;

                var outcome = GetDayOutcomeFromMinutes(r, minutes, dailyTpPct, dailySlPct, nyTz);
                if (outcome == DayOutcome.None)
                    continue;

                bool isSlDay = outcome == DayOutcome.SlFirst;

                if (r.SlHighDecision is not bool predHigh)
                    continue;

                double? probOpt = r.SlProb;
                bool hasScore = probOpt.HasValue && probOpt.Value > 0.0;

                if (hasScore)
                {
                    double prob = probOpt!.Value;
                    scoredDays++;

                    prPoints.Add((prob, isSlDay ? 1 : 0));
                    thrDays.Add(new SlThresholdDay { IsSlDay = isSlDay, Prob = prob });
                }

                if (!isSlDay)
                {
                    if (predHigh) tp_high++; else tp_low++;
                }
                else
                {
                    if (predHigh) sl_high++; else sl_low++;
                    if (predHigh) slSaved++;
                }
            }

            var confusion = slStats.Confusion;
            confusion.TpLow = tp_low;
            confusion.TpHigh = tp_high;
            confusion.SlLow = sl_low;
            confusion.SlHigh = sl_high;
            confusion.SlSaved = slSaved;
            confusion.TotalSignalDays = totalSignalDays;
            confusion.ScoredDays = scoredDays;
            confusion.TotalOutcomeDays = thrDays.Count;

            int totalSl = thrDays.Count(d => d.IsSlDay);
            int totalTp = thrDays.Count - totalSl;

            confusion.TotalSlDays = totalSl;
            confusion.TotalTpDays = totalTp;

            int tp = sl_high;
            int fn = sl_low;
            int fp = tp_high;
            int tn = tp_low;

            double tpr = tp + fn > 0 ? (double)tp / (tp + fn) : 0.0;
            double fpr = fp + tn > 0 ? (double)fp / (fp + tn) : 0.0;
            double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0.0;
            double recall = tpr;
            double f1 = precision + recall > 0 ? 2.0 * precision * recall / (precision + recall) : 0.0;

            double coverage = totalSignalDays > 0 ? (double)scoredDays / totalSignalDays : 0.0;
            double prAuc = prPoints.Count >= 2 ? ComputePrAuc(prPoints) : 0.0;

            slStats.Metrics = new SlMetricsStats
            {
                Coverage = coverage,
                Tpr = tpr,
                Fpr = fpr,
                Precision = precision,
                Recall = recall,
                F1 = f1,
                PrAuc = prAuc
            };

            slStats.Thresholds.Clear();
            if (thrDays.Count == 0)
                return;

            double[] thresholds = { 0.30, 0.40, 0.50, 0.60 };

            foreach (double thr in thresholds)
            {
                int highSl = thrDays.Count(d => d.IsSlDay && d.Prob >= thr);
                int highTp = thrDays.Count(d => !d.IsSlDay && d.Prob >= thr);
                int highTotal = highSl + highTp;

                double tprLocal = totalSl > 0 ? (double)highSl / totalSl * 100.0 : 0.0;
                double fprLocal = totalTp > 0 ? (double)highTp / totalTp * 100.0 : 0.0;
                double highFrac = thrDays.Count > 0 ? (double)highTotal / thrDays.Count * 100.0 : 0.0;

                bool isGood = tprLocal >= 60.0 && fprLocal <= 40.0;

                slStats.Thresholds.Add(new SlThresholdStatsRow
                {
                    Threshold = thr,
                    TprPct = tprLocal,
                    FprPct = fprLocal,
                    PredHighPct = highFrac,
                    HighTotal = highTotal,
                    TotalDays = thrDays.Count,
                    IsGood = isGood,
                    HighSlDays = highSl,
                    HighTpDays = highTp,
                    TotalSlDays = totalSl,
                    TotalTpDays = totalTp
                });
            }
        }

        private static DayOutcome GetDayOutcomeFromMinutes(
            BacktestRecord r,
            IReadOnlyList<Candle1m> allMinutesSorted,
            double tpPct,
            double slPct,
            TimeZoneInfo nyTz)
        {
            if (!TryGetPredDirection(r, out var goLong, out var goShort))
                return DayOutcome.None;

            double entry = r.Entry;
            if (entry <= 0.0)
                return DayOutcome.None;

            DateTime from = EntryUtcOf(r).Value;

            if (!NyWindowing.TryComputeBaselineExitUtc(EntryUtcOf(r), nyTz, out var toExit))
                return DayOutcome.None;

            DateTime to = toExit.Value;

            if (allMinutesSorted.Count == 0)
                return DayOutcome.None;

            int start = LowerBound(allMinutesSorted, from);
            int end = LowerBound(allMinutesSorted, to);

            if (start >= end)
                return DayOutcome.None;

            if (goLong)
            {
                double tp = entry * (1.0 + tpPct);
                double sl = slPct > 1e-9 ? entry * (1.0 - slPct) : double.NaN;

                for (int i = start; i < end; i++)
                {
                    var m = allMinutesSorted[i];

                    bool hitTp = m.High >= tp;
                    bool hitSl = !double.IsNaN(sl) && m.Low <= sl;
                    if (!hitTp && !hitSl) continue;

                    if (hitSl) return DayOutcome.SlFirst;
                    return DayOutcome.TpFirst;
                }
            }
            else if (goShort)
            {
                double tp = entry * (1.0 - tpPct);
                double sl = slPct > 1e-9 ? entry * (1.0 + slPct) : double.NaN;

                for (int i = start; i < end; i++)
                {
                    var m = allMinutesSorted[i];

                    bool hitTp = m.Low <= tp;
                    bool hitSl = !double.IsNaN(sl) && m.High >= sl;
                    if (!hitTp && !hitSl) continue;

                    if (hitSl) return DayOutcome.SlFirst;
                    return DayOutcome.TpFirst;
                }
            }

            return DayOutcome.None;
        }

        private static int LowerBound(IReadOnlyList<Candle1m> xs, DateTime tUtc)
        {
            int lo = 0;
            int hi = xs.Count;

            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (xs[mid].OpenTimeUtc < tUtc) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private static bool TryGetPredDirection(BacktestRecord r, out bool predUp, out bool predDown)
        {
            bool microUp = r.PredMicroUp == true;
            bool microDown = r.PredMicroDown == true;

            if (microUp && microDown)
                throw new InvalidOperationException($"[model-stats] microUp && microDown одновременно на {EntryUtcOf(r).Value:O}.");

            predUp = r.PredLabel == 2 || (r.PredLabel == 1 && microUp);
            predDown = r.PredLabel == 0 || (r.PredLabel == 1 && microDown);

            return predUp || predDown;
        }

        private static double ComputePrAuc(List<(double Score, int Label)> points)
        {
            if (points == null || points.Count == 0)
                return 0.0;

            int totalPos = points.Count(p => p.Label == 1);
            if (totalPos == 0)
                return 0.0;

            int totalNeg = points.Count - totalPos;

            var sorted = points.OrderByDescending(p => p.Score).ToList();

            int tp = 0, fp = 0;

            double prevRecall = 0.0;
            double basePrecision = (double)totalPos / (totalPos + totalNeg);
            double prevPrecision = basePrecision;

            double auc = 0.0;

            foreach (var p in sorted)
            {
                if (p.Label == 1) tp++; else fp++;

                double recall = (double)tp / totalPos;
                double precision = tp + fp > 0 ? (double)tp / (tp + fp) : prevPrecision;

                double deltaR = recall - prevRecall;
                if (deltaR > 0)
                    auc += deltaR * (precision + prevPrecision) * 0.5;

                prevRecall = recall;
                prevPrecision = precision;
            }

            return auc;
        }
    }
}

