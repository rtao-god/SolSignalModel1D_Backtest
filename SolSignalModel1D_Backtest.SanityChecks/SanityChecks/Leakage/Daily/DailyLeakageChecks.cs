using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily
{
    public static class DailyLeakageChecks
    {
        public static SelfCheckResult CheckDailyTrainVsOosAndShuffle(
            IReadOnlyList<BacktestRecord> records,
            TrainUntilUtc trainUntilUtc,
            TimeZoneInfo nyTz)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (trainUntilUtc.Value == default) throw new ArgumentException("trainUntilUtc must be initialized.", nameof(trainUntilUtc));

            if (records.Count == 0)
                return SelfCheckResult.Ok("[daily] no records — skipped.");

            var ordered = records
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: static r => r.Causal.RawEntryUtc,
                trainUntilExitDayKeyUtc: trainUntilUtc.ExitDayKeyUtc,
                nyTz: nyTz);

            var warnings = new List<string>();
            var errors = new List<string>();

            if (split.Excluded.Count > 0)
                warnings.Add($"[daily] excluded={split.Excluded.Count} (baseline-exit undefined).");

            var trainPairs = FilterValidTriClassPairs(split.Train);
            var oosPairs = FilterValidTriClassPairs(split.Oos);
            var allPairs = FilterValidTriClassPairs(ordered);

            if (oosPairs.Count == 0)
                warnings.Add("[daily] OOS-часть пуста (по trainUntil/baseline-exit).");

            var trainAcc = ComputeAccuracyPct(trainPairs);
            var oosAcc = ComputeAccuracyPct(oosPairs);
            var allAcc = ComputeAccuracyPct(allPairs);

            if (trainPairs.Count >= 50 && trainAcc >= 85.0)
                errors.Add($"[daily] train accuracy suspiciously high: {trainAcc:0.0}% (n={trainPairs.Count}).");

            if (oosPairs.Count >= 100 && oosAcc >= 75.0)
                errors.Add($"[daily] OOS accuracy suspiciously high: {oosAcc:0.0}% (n={oosPairs.Count}).");

            var shuffledAcc = ComputeShuffleAccuracyPct(trainPairs.Count >= 50 ? trainPairs : (oosPairs.Count > 0 ? oosPairs : trainPairs));
            if (shuffledAcc >= 55.0)
                errors.Add($"[daily] shuffled-label accuracy too high: {shuffledAcc:0.0}%.");

            var summary =
                $"[daily] train={trainPairs.Count}, oos={oosPairs.Count}, excluded={split.Excluded.Count}, " +
                $"acc_train={trainAcc:0.0}%, acc_oos={oosAcc:0.0}%, acc_all={allAcc:0.0}%, acc_shuffle={shuffledAcc:0.0}%";

            var res = new SelfCheckResult
            {
                Success = errors.Count == 0,
                Summary = summary
            };

            res.Errors.AddRange(errors);
            res.Warnings.AddRange(warnings);

            res.Metrics["daily.acc_all"] = allAcc / 100.0;
            res.Metrics["daily.acc_train"] = trainAcc / 100.0;
            res.Metrics["daily.acc_oos"] = oosAcc / 100.0;
            res.Metrics["daily.acc_shuffle"] = shuffledAcc / 100.0;

            res.Metrics["daily.n_all"] = allPairs.Count;
            res.Metrics["daily.n_train"] = trainPairs.Count;
            res.Metrics["daily.n_oos"] = oosPairs.Count;
            res.Metrics["daily.n_excluded"] = split.Excluded.Count;

            return res;
        }

        private static List<(int TrueLabel, int PredLabel)> FilterValidTriClassPairs(IReadOnlyList<BacktestRecord> rows)
        {
            var res = new List<(int TrueLabel, int PredLabel)>(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int t = r.TrueLabel;
                int p = r.PredLabel;

                if (t is >= 0 and <= 2 && p is >= 0 and <= 2)
                    res.Add((t, p));
            }

            return res;
        }

        private static double ComputeAccuracyPct(IReadOnlyList<(int TrueLabel, int PredLabel)> pairs)
        {
            if (pairs == null || pairs.Count == 0) return 0.0;

            int correct = 0;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].TrueLabel == pairs[i].PredLabel)
                    correct++;
            }

            return (double)correct / pairs.Count * 100.0;
        }

        private static double ComputeShuffleAccuracyPct(IReadOnlyList<(int TrueLabel, int PredLabel)> pairs)
        {
            if (pairs == null || pairs.Count == 0) return 0.0;

            var labels = new int[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
                labels[i] = pairs[i].TrueLabel;

            var rng = new Random(123);

            for (int i = labels.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (labels[i], labels[j]) = (labels[j], labels[i]);
            }

            int correct = 0;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (labels[i] == pairs[i].PredLabel)
                    correct++;
            }

            return (double)correct / pairs.Count * 100.0;
        }
    }
}
