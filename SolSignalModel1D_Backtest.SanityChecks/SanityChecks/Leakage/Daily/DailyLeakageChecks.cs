using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily
{
    /// <summary>
    /// Sanity-проверки для дневной модели:
    /// - разбиение Train/OOS строго по baseline-exit (через NyWindowing);
    /// - сравнение accuracies Train vs OOS;
    /// - сравнение с рандомной "shuffle"-моделью.
    /// </summary>
    public static class DailyLeakageChecks
    {
        public static SelfCheckResult CheckDailyTrainVsOosAndShuffle(
            IReadOnlyList<BacktestRecord> records,
            DateTime trainUntilUtc,
            TimeZoneInfo nyTz)
        {
            if (records == null || records.Count == 0)
            {
                return SelfCheckResult.Ok("[daily] нет BacktestRecord'ов — пропускаем дневные проверки.");
            }

            if (trainUntilUtc == default)
                throw new ArgumentException("[daily] trainUntilUtc must be initialized (non-default).", nameof(trainUntilUtc));
            if (trainUntilUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("[daily] trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof(trainUntilUtc));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var ordered = records
                .OrderBy(r => r.Causal.DayKeyUtc.Value)
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExit(ordered, r => r.Causal.EntryUtc, trainUntilUtc, nyTz);

            var train = split.Train;
            var oos = split.Oos;
            var excluded = split.Excluded;

            var eligible = new List<BacktestRecord>(train.Count + oos.Count);
            eligible.AddRange(train);
            eligible.AddRange(oos);

            var trainUntilIso = NyTrainSplit.ToIsoDate(trainUntilUtc);

            if (eligible.Count == 0)
            {
                return SelfCheckResult.Fail(
                    $"[daily] eligible=0 (train+oos), excluded={excluded.Count}. " +
                    $"Проверь контракт entryUtc и baseline-exit. trainUntil={trainUntilIso}");
            }

            var warnings = new List<string>();
            var errors = new List<string>();

            if (excluded.Count > 0)
            {
                warnings.Add(
                    $"[daily] excluded={excluded.Count} (no baseline-exit by contract). " +
                    "Эти дни исключены из метрик; проверь weekend/дыры/несогласованность entryUtc.");
            }

            if (oos.Count == 0)
            {
                warnings.Add(
                    $"[daily] OOS-часть пуста (нет дней с exit > {trainUntilIso}). Метрики будут train-like.");
            }

            double trainAcc = train.Count > 0 ? ComputeAccuracy(train) : double.NaN;
            double oosAcc = oos.Count > 0 ? ComputeAccuracy(oos) : double.NaN;
            double allAcc = ComputeAccuracy(eligible);

            double shuffleAcc = ComputeShuffleAccuracy(eligible, classesCount: 3, seed: 42);

            string summary =
                $"[daily] eligible={eligible.Count}, excluded={excluded.Count}, " +
                $"train={train.Count}, oos={oos.Count}, trainUntil(exit<=){trainUntilIso}, " +
                $"acc_all={allAcc:P1}, acc_train={trainAcc:P1}, acc_oos={oosAcc:P1}, acc_shuffle≈{shuffleAcc:P1}";

            if (!double.IsNaN(trainAcc) && train.Count >= 200 && trainAcc > 0.95)
            {
                errors.Add($"[daily] train accuracy {trainAcc:P1} при {train.Count} дней — подозрение на утечку.");
            }

            if (!double.IsNaN(oosAcc) && oos.Count >= 100 && oosAcc > 0.90)
            {
                errors.Add($"[daily] OOS accuracy {oosAcc:P1} при {oos.Count} дней — подозрение на утечку.");
            }

            if (!double.IsNaN(allAcc) && !double.IsNaN(shuffleAcc) && allAcc < shuffleAcc + 0.05)
            {
                warnings.Add($"[daily] accuracy по eligible {allAcc:P1} почти не лучше shuffle {shuffleAcc:P1}.");
            }

            var result = new SelfCheckResult
            {
                Success = errors.Count == 0,
                Summary = summary
            };

            result.Errors.AddRange(errors);
            result.Warnings.AddRange(warnings);

            result.Metrics["daily.eligible"] = eligible.Count;
            result.Metrics["daily.excluded"] = excluded.Count;

            result.Metrics["daily.acc_all"] = allAcc;
            result.Metrics["daily.acc_train"] = trainAcc;
            result.Metrics["daily.acc_oos"] = oosAcc;
            result.Metrics["daily.acc_shuffle"] = shuffleAcc;

            return result;
        }

        private static double ComputeAccuracy(IReadOnlyList<BacktestRecord> records)
        {
            if (records.Count == 0) return double.NaN;

            int ok = 0;
            foreach (var r in records)
            {
                if (r.PredLabel_Total == r.TrueLabel)
                    ok++;
            }

            return ok / (double)records.Count;
        }

        private static double ComputeShuffleAccuracy(
            IReadOnlyList<BacktestRecord> records,
            int classesCount,
            int seed)
        {
            if (records.Count == 0 || classesCount <= 1) return double.NaN;

            var rnd = new Random(seed);
            int ok = 0;

            foreach (var r in records)
            {
                int randomLabel = rnd.Next(classesCount);
                if (randomLabel == r.TrueLabel)
                    ok++;
            }

            return ok / (double)records.Count;
        }
    }
}
