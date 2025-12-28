using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Rows
{
    /// <summary>
    /// Self-check на утечку: пытается найти фичи, которые численно совпадают
    /// с будущими таргетами (MaxHigh24/MinLow24/Close24/первая 1m после exit и т.п.).
    ///
    /// Контракт:
    /// - фичи читаем только из Causal.FeaturesVector;
    /// - будущие таргеты берём только из omniscient части записи;
    /// - baseline-exit считается от EntryUtc (timestamp).
    /// </summary>
    public static class RowFeatureLeakageChecks
    {
        private const double ZeroValueTol = 1e-4;

        private static readonly IReadOnlyList<string> FeatureNames = CausalDataRow.FeatureNames;

        public static SelfCheckResult CheckRowFeaturesAgainstFuture(SelfCheckContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var result = new SelfCheckResult
            {
                Success = true,
                Summary = "[rows-leak] features vs future targets"
            };

            var allRows = ctx.Records;
            var sol1m = ctx.Sol1m;
            var nyTz = ctx.NyTz;

            if (allRows == null || allRows.Count == 0)
                return SelfCheckResult.Ok("[rows-leak] skip: AllRows is empty.");

            List<Candle1m>? sol1mSorted = null;
            if (sol1m != null && sol1m.Count > 0)
            {
                sol1mSorted = sol1m
                    .OrderBy(c => c.OpenTimeUtc)
                    .ToList();
            }

            var futureTargetsByDayKey = new Dictionary<DateTime, List<(string Name, double Value)>>();

            foreach (var rec in allRows)
            {
                if (rec == null)
                    throw new InvalidOperationException("[rows-leak] AllRows contains null item.");

                var entryUtc = CausalTimeKey.EntryUtc(rec);
                var dayKey = entryUtc.EntryDayKeyUtc.Value;

                var targets = new List<(string Name, double Value)>();

                // double? → кодируем null как NaN, чтобы ниже это попало в invalidTargetValueCount.
                targets.Add(("MaxHigh24", rec.MaxHigh24));
                targets.Add(("MinLow24", rec.MinLow24));
                targets.Add(("Close24", rec.Close24));

                double close24 = rec.Close24;

                if (double.IsFinite(rec.Entry) && rec.Entry > 0.0 && double.IsFinite(close24))
                {
                    double solFwd1 = close24 / rec.Entry - 1.0;
                    targets.Add(("SolFwd1(calc:Close24/Entry-1)", solFwd1));
                }

                if (sol1mSorted != null && nyTz != null)
                {
                    var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc(entryUtc, nyTz).Value;
                    var future1m = FindFirstMinuteAfter(sol1mSorted, exitUtc);
                    if (future1m != null)
                        targets.Add(("Future1mAfterExit", future1m.Close));
                }

                futureTargetsByDayKey[dayKey] = targets;
            }

            var suspiciousMatches = new List<MatchInfo>(capacity: 256);

            int emptyFeatureVectorCount = 0;
            int invalidFeatureValueCount = 0;
            int invalidTargetValueCount = 0;

            var invalidFeatureSamples = new List<string>(capacity: 8);
            var invalidTargetSamples = new List<string>(capacity: 8);
            var emptyVectorSamples = new List<string>(capacity: 8);

            foreach (var rec in allRows)
            {
                if (rec == null) continue;

                var dayKey = CausalTimeKey.EntryDayKeyUtc(rec).Value;

                if (!futureTargetsByDayKey.TryGetValue(dayKey, out var targets) || targets.Count == 0)
                    continue;

                var vec = rec.Causal.FeaturesVector;
                if (vec.IsEmpty)
                {
                    emptyFeatureVectorCount++;
                    if (emptyVectorSamples.Count < 5)
                        emptyVectorSamples.Add($"[rows-leak] empty FeaturesVector: dayKey={dayKey:O}");
                    continue;
                }

                var feats = vec.Span;

                for (int fi = 0; fi < feats.Length; fi++)
                {
                    double fVal = feats[fi];
                    if (double.IsNaN(fVal) || double.IsInfinity(fVal))
                    {
                        invalidFeatureValueCount++;
                        if (invalidFeatureSamples.Count < 5)
                        {
                            string featName =
                                fi >= 0 && fi < FeatureNames.Count
                                    ? FeatureNames[fi]
                                    : $"feat{fi}";

                            invalidFeatureSamples.Add(
                                $"[rows-leak] invalid feature value: dayKey={dayKey:O}, featureIndex={fi} ({featName}), featureVal={fVal}");
                        }
                        continue;
                    }

                    foreach (var (name, tVal) in targets)
                    {
                        if (double.IsNaN(tVal) || double.IsInfinity(tVal))
                        {
                            invalidTargetValueCount++;
                            if (invalidTargetSamples.Count < 5)
                            {
                                invalidTargetSamples.Add(
                                    $"[rows-leak] invalid target value: dayKey={dayKey:O}, target={name}, targetVal={tVal}");
                            }
                            continue;
                        }

                        if (IsNearlyEqual(fVal, tVal))
                        {
                            suspiciousMatches.Add(new MatchInfo
                            {
                                Date = dayKey,
                                FeatureIndex = fi,
                                TargetName = name,
                                FeatureVal = fVal,
                                TargetVal = tVal,

                                TrueLabel = rec.TrueLabel,
                                MinMove = rec.MinMove,
                                RegimeDown = rec.RegimeDown,
                                IsMorning = rec.Causal.IsMorning == true
                            });
                        }
                    }
                }
            }

            if (emptyFeatureVectorCount > 0 || invalidFeatureValueCount > 0 || invalidTargetValueCount > 0)
            {
                result.Success = false;

                if (emptyFeatureVectorCount > 0)
                {
                    result.Errors.Add($"[rows-leak] invalid input: empty FeaturesVector count={emptyFeatureVectorCount}.");
                    foreach (var s in emptyVectorSamples) result.Errors.Add(s);
                }

                if (invalidFeatureValueCount > 0)
                {
                    result.Errors.Add($"[rows-leak] invalid input: NaN/Infinity in features count={invalidFeatureValueCount}.");
                    foreach (var s in invalidFeatureSamples) result.Errors.Add(s);
                }

                if (invalidTargetValueCount > 0)
                {
                    result.Errors.Add($"[rows-leak] invalid input: NaN/Infinity in future targets count={invalidTargetValueCount}.");
                    foreach (var s in invalidTargetSamples) result.Errors.Add(s);
                }
            }

            if (suspiciousMatches.Count == 0)
            {
                result.Summary += " → no suspicious equality detected.";
                return result;
            }

            const int MinMatchesPerFeatureTarget = 5;
            const double MinMatchFrac = 0.005;
            const int MinNonZeroMatchesPerFeatureTarget = 3;

            int totalRows = allRows.Count;

            var groupedRaw = suspiciousMatches
                .GroupBy(m => new { m.FeatureIndex, m.TargetName })
                .Select(g =>
                {
                    var all = g.ToList();
                    var nonZero = all
                        .Where(m =>
                            Math.Abs(m.FeatureVal) > ZeroValueTol ||
                            Math.Abs(m.TargetVal) > ZeroValueTol)
                        .ToList();

                    return new FeatureTargetGroup
                    {
                        FeatureIndex = g.Key.FeatureIndex,
                        TargetName = g.Key.TargetName,
                        AllMatches = all,
                        NonZeroMatches = nonZero
                    };
                })
                .Where(g =>
                    g.AllMatches.Count >= MinMatchesPerFeatureTarget &&
                    g.AllMatches.Count / (double)totalRows >= MinMatchFrac &&
                    g.NonZeroMatches.Count >= MinNonZeroMatchesPerFeatureTarget)
                .ToList();

            if (groupedRaw.Count == 0)
            {
                result.Summary += " → only rare coincidences, treating as noise.";
                return result;
            }

            var realLeaks = new List<FeatureTargetGroup>();
            var noiseGroups = new List<FeatureTargetGroup>();

            foreach (var g in groupedRaw)
            {
                bool isBinaryFeature =
                    g.AllMatches.All(m =>
                        Math.Abs(m.FeatureVal) <= 1.0 + 1e-6 &&
                        (Math.Abs(m.FeatureVal) <= ZeroValueTol ||
                         Math.Abs(m.FeatureVal - 1.0) <= ZeroValueTol));

                double fracZeroTarget =
                    g.AllMatches.Count(m => Math.Abs(m.TargetVal) <= ZeroValueTol)
                    / (double)g.AllMatches.Count;

                bool targetMostlyZero = fracZeroTarget >= 0.8;

                if (isBinaryFeature && targetMostlyZero)
                    noiseGroups.Add(g);
                else
                    realLeaks.Add(g);
            }

            foreach (var g in noiseGroups)
                LogGroup(result.Warnings, totalRows, g, "[rows-leak] noise group");

            if (realLeaks.Count == 0)
            {
                result.Summary += " → only binary/near-zero groups, treated as noise.";
                return result;
            }

            result.Success = false;
            foreach (var g in realLeaks.OrderByDescending(x => x.NonZeroMatches.Count))
                LogGroup(result.Errors, totalRows, g, "[rows-leak] possible feature leak");

            result.Summary += $" → FAILED: suspicious groups={realLeaks.Count}.";
            return result;
        }

        private static void LogGroup(List<string> sink, int totalRows, FeatureTargetGroup g, string headerPrefix)
        {
            int countTotal = g.AllMatches.Count;
            int countNonZero = g.NonZeroMatches.Count;
            double fracTotal = countTotal / (double)totalRows;
            double fracNonZero = countNonZero / (double)totalRows;

            string featName =
                g.FeatureIndex >= 0 && g.FeatureIndex < FeatureNames.Count
                    ? FeatureNames[g.FeatureIndex]
                    : $"feat{g.FeatureIndex}";

            var header =
                $"{headerPrefix}: featureIndex={g.FeatureIndex} ({featName}), " +
                $"target={g.TargetName}, matches={countTotal}, frac={fracTotal:P2}, " +
                $"nonZero={countNonZero}, fracNonZero={fracNonZero:P2}";

            sink.Add(header);

            foreach (var sample in g.NonZeroMatches.OrderBy(x => x.Date).Take(5))
            {
                var line =
                    $"[rows-leak]   dayKey={sample.Date:O}, " +
                    $"featureVal={sample.FeatureVal:0.########}, " +
                    $"targetVal={sample.TargetVal:0.########}, " +
                    $"trueLabel={sample.TrueLabel}, minMove={sample.MinMove:0.####}, " +
                    $"regimeDown={sample.RegimeDown}, isMorning={sample.IsMorning}";
                sink.Add(line);
            }
        }

        private static Candle1m? FindFirstMinuteAfter(List<Candle1m> minutes, DateTime t)
        {
            int lo = 0;
            int hi = minutes.Count - 1;
            int ans = -1;

            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (minutes[mid].OpenTimeUtc > t)
                {
                    ans = mid;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return ans >= 0 ? minutes[ans] : null;
        }

        private static bool IsNearlyEqual(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y))
                return false;

            const double AbsTol = 1e-8;
            const double RelTol = 1e-4;

            double diff = Math.Abs(x - y);
            if (diff <= AbsTol)
                return true;

            double max = Math.Max(Math.Abs(x), Math.Abs(y));
            if (max == 0.0)
                return diff == 0.0;

            return diff / max <= RelTol;
        }

        private sealed class MatchInfo
        {
            public DateTime Date { get; set; }
            public int FeatureIndex { get; set; }
            public string TargetName { get; set; } = string.Empty;
            public double FeatureVal { get; set; }
            public double TargetVal { get; set; }

            public int TrueLabel { get; set; }
            public double MinMove { get; set; }
            public bool RegimeDown { get; set; }
            public bool IsMorning { get; set; }
        }

        private sealed class FeatureTargetGroup
        {
            public int FeatureIndex { get; set; }
            public string TargetName { get; set; } = string.Empty;
            public List<MatchInfo> AllMatches { get; set; } = new();
            public List<MatchInfo> NonZeroMatches { get; set; } = new();
        }
    }
}

