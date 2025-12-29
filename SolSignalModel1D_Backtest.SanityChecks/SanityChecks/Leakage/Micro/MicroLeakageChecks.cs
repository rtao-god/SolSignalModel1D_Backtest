using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Micro
{
    /// <summary>
    /// Sanity-проверки для микро-слоя:
    /// - достаточное число размеченных микро-дней;
    /// - покрытие прогнозами;
    /// - сравнение accuracies train vs OOS и с рандомным baseline.
    /// </summary>
    public static class MicroLeakageChecks
    {
        public static SelfCheckResult CheckMicroLayer(SelfCheckContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var mornings = ctx.Mornings ?? Array.Empty<LabeledCausalRow>();
            var records = ctx.Records ?? Array.Empty<BacktestRecord>();

            if (mornings.Count == 0 || records.Count == 0)
            {
                return SelfCheckResult.Ok("[micro] нет данных для микро-слоя (mornings/records пусты).");
            }

            var labeled = mornings
                .Where(r => r.MicroTruth.HasValue)
                .OrderBy(r => CausalTimeKey.EntryDayKeyUtc(r))
                .ToList();

            if (labeled.Count < 20)
            {
                return SelfCheckResult.Ok(
                    $"[micro] размеченных микро-дней слишком мало ({labeled.Count}), пропускаем sanity-проверку.");
            }

            var factByDayKey = labeled.ToDictionary(r => CausalTimeKey.EntryDayKeyUtc(r), r => r);

            // Пары собираем по exit-day-key (baseline-exit), чтобы сравнение train vs OOS было по каноничной границе.
            var pairs = new List<(ExitDayKeyUtc ExitDayKeyUtc, bool PredUp, bool FactUp)>();

            foreach (var rec in records)
            {
                var dayKey = CausalTimeKey.EntryDayKeyUtc(rec);

                if (!factByDayKey.TryGetValue(dayKey, out var row))
                    continue;

                if (!rec.PredMicroUp && !rec.PredMicroDown)
                    continue;

                var exitDayKeyUtc = CoreNyWindowing.ComputeExitDayKeyUtc(rec.EntryUtc, CoreNyWindowing.NyTz);

                pairs.Add((
                    ExitDayKeyUtc: exitDayKeyUtc,
                    PredUp: rec.PredMicroUp,
                    FactUp: row.MicroTruth.Value == MicroTruthDirection.Up));
            }

            // Граница сравнения для micro-check: day-key границы trainUntil (exit-day-key по контракту пайплайна).
            var trainUntilExitDayKey = ctx.TrainUntilExitDayKeyUtc;

            int labeledExcluded = 0;
            int labeledOosCount = 0;

            for (int i = 0; i < labeled.Count; i++)
            {
                var entryUtc = new EntryUtc(labeled[i].Causal.EntryUtc.Value);

                if (!CoreNyWindowing.TryComputeExitDayKeyUtc(entryUtc, CoreNyWindowing.NyTz, out var exitDayKeyUtc))
                {
                    labeledExcluded++;
                    continue;
                }

                if (exitDayKeyUtc.Value > trainUntilExitDayKey.Value)
                    labeledOosCount++;
            }

            if (pairs.Count < 20)
            {
                return SelfCheckResult.Ok(
                    $"[micro] слишком мало дней, где есть и микро-прогноз, и path-based разметка ({pairs.Count}).");
            }

            var train = pairs.Where(p => p.ExitDayKeyUtc.Value <= trainUntilExitDayKey.Value).ToList();
            var oos = pairs.Where(p => p.ExitDayKeyUtc.Value > trainUntilExitDayKey.Value).ToList();

            double accAll = ComputeAccuracy(pairs);
            double accTrain = ComputeAccuracy(train);
            double accOos = ComputeAccuracy(oos);

            const double shuffleAcc = 0.5;

            var warnings = new List<string>();
            var errors = new List<string>();

            if (train.Count < 30)
            {
                warnings.Add($"[micro] train-выборка микро-слоя мала ({train.Count}), статистика шумная.");
            }

            if (oos.Count == 0)
            {
                if (labeledOosCount > 0)
                {
                    warnings.Add(
                        "[micro] OOS-часть по микро-прогнозам пуста " +
                        $"(есть микро-факты после границы, labeledOos={labeledOosCount}, predOos=0).");
                }
                else
                {
                    warnings.Add(
                        "[micro] OOS-часть по микро-фактам пуста " +
                        $"(нет микро-дней после границы по baseline-exit, excluded={labeledExcluded}).");
                }
            }

            if (!double.IsNaN(accOos) && oos.Count >= 50 && accOos > 0.90)
            {
                errors.Add($"[micro] OOS accuracy {accOos:P1} при {oos.Count} дней — подозрение на утечку в микро-слое.");
            }

            if (!double.IsNaN(accAll) && accAll < shuffleAcc + 0.05)
            {
                warnings.Add($"[micro] accuracy по всей выборке {accAll:P1} почти не лучше случайной модели {shuffleAcc:P1}.");
            }

            int predUpCount = pairs.Count(p => p.PredUp);
            int predDownCount = pairs.Count - predUpCount;

            if (predUpCount == 0 || predDownCount == 0)
            {
                warnings.Add("[micro] микро-слой почти всегда даёт один и тот же знак (up или down) — проверь пороги и покрытие.");
            }

            string summary =
                $"[micro] pairs={pairs.Count}, train={train.Count}, oos={oos.Count}, " +
                $"acc_all={accAll:P1}, acc_train={accTrain:P1}, acc_oos={accOos:P1}";

            var result = new SelfCheckResult
            {
                Success = errors.Count == 0,
                Summary = summary
            };
            result.Errors.AddRange(errors);
            result.Warnings.AddRange(warnings);
            return result;
        }

        private static double ComputeAccuracy(IReadOnlyList<(ExitDayKeyUtc ExitDayKeyUtc, bool PredUp, bool FactUp)> items)
        {
            if (items == null || items.Count == 0)
                return double.NaN;

            int ok = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].PredUp == items[i].FactUp)
                    ok++;
            }

            return ok / (double)items.Count;
        }
    }
}

