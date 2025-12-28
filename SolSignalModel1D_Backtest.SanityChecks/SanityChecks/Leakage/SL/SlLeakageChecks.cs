using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.SL
{
    public static class SlLeakageChecks
    {
        public static SelfCheckResult CheckSlLayer(SelfCheckContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var records = ctx.Records ?? Array.Empty<BacktestRecord>();
            var candles1h = ctx.SolAll1h ?? Array.Empty<Candle1h>();

            if (records.Count == 0 || candles1h.Count == 0)
            {
                var missing = new SelfCheckResult
                {
                    Success = false,
                    Summary = "[sl] отсутствуют входные данные для проверки SL-слоя."
                };

                if (records.Count == 0) missing.Errors.Add("[sl] ctx.Records is empty.");
                if (candles1h.Count == 0) missing.Errors.Add("[sl] ctx.SolAll1h is empty.");

                return missing;
            }

            if (ctx.TrainUntilExitDayKeyUtc.IsDefault)
                throw new InvalidOperationException("[sl] ctx.TrainUntilExitDayKeyUtc is default (uninitialized).");

            var trainUntilExitDayKeyUtc = ctx.TrainUntilExitDayKeyUtc;

            var samples = new List<SlSample>();

            foreach (var rec in records)
            {
                var c = rec.Causal;
                var f = rec.Forward;

                bool goLong = c.PredLabel == 2 || (c.PredLabel == 1 && c.PredMicroUp);
                bool goShort = c.PredLabel == 0 || (c.PredLabel == 1 && c.PredMicroDown);

                if (!goLong && !goShort)
                    continue;

                double slProb = c.SlProb
                    ?? throw new InvalidOperationException($"[sl] SlProb is null for day={c.EntryDayKeyUtc} (SL layer not evaluated).");

                bool slHigh = c.SlHighDecision
                    ?? throw new InvalidOperationException($"[sl] SlHighDecision is null for day={c.EntryDayKeyUtc} (SL layer not evaluated).");

                if (slProb < 0.0 || slProb > 1.0)
                {
                    var badRange = new SelfCheckResult
                    {
                        Success = false,
                        Summary = $"[sl] обнаружена SlProb вне диапазона [0,1]: {slProb:0.000} на day={c.EntryDayKeyUtc}."
                    };
                    badRange.Errors.Add("[sl] SlProb должен лежать в [0,1].");
                    return badRange;
                }

                double dayMinMove = c.MinMove;
                if (double.IsNaN(dayMinMove) || double.IsInfinity(dayMinMove) || dayMinMove <= 0.0)
                {
                    var bad = new SelfCheckResult
                    {
                        Success = false,
                        Summary = $"[sl] invalid Causal.MinMove: {dayMinMove:0.######} for day={c.EntryDayKeyUtc}."
                    };
                    bad.Errors.Add("[sl] Causal.MinMove must be finite and > 0. Fix upstream MinMove computation/NyWindowing.");
                    return bad;
                }

                bool strongSignal = c.PredLabel == 0 || c.PredLabel == 2;

                var outcome = HourlyTradeEvaluator.EvaluateOne(
                    candles1h,
                    entryUtc: c.EntryUtc.Value,
                    goLong: goLong,
                    goShort: goShort,
                    entryPrice: f.Entry,
                    dayMinMove: dayMinMove,
                    strongSignal: strongSignal,
                    nyTz: ctx.NyTz);

                if (outcome.Result != HourlyTradeResult.SlFirst &&
                    outcome.Result != HourlyTradeResult.TpFirst)
                {
                    continue;
                }

                bool trueHighRisk = outcome.Result == HourlyTradeResult.SlFirst;

                samples.Add(new SlSample
                {
                    EntryUtc = c.EntryUtc.Value,
                    SlProb = slProb,
                    SlHighDecision = slHigh,
                    TrueHighRisk = trueHighRisk
                });
            }

            if (samples.Count == 0)
            {
                return SelfCheckResult.Ok("[sl] нет сделок с однозначным исходом TP/SL для оценки SL-слоя.");
            }

            bool allDefault = samples.All(s => s.SlProb == 0.0 && !s.SlHighDecision);
            if (allDefault)
            {
                return SelfCheckResult.Ok(
                    "[sl] все SlProb≈0 и SlHighDecision=false — похоже, SL-модель не применялась, sanity-проверка пропущена.");
            }

            if (samples.Count < 50)
            {
                return SelfCheckResult.Ok(
                    $"[sl] недостаточно сделок с однозначным исходом для оценки SL ({samples.Count}), sanity-проверка пропущена.");
            }

            var ordered = samples.OrderBy(s => s.EntryUtc).ToList();

            var sSplit = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: s => new EntryUtc(s.EntryUtc),
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: ctx.NyTz);

            var train = sSplit.Train;
            var oos = sSplit.Oos;

            var warnings = new List<string>();
            var errors = new List<string>();

            if (sSplit.Excluded.Count > 0)
            {
                warnings.Add($"[sl] excluded={sSplit.Excluded.Count} сделок: baseline-exit не определён (weekend по контракту).");
            }

            if (train.Count < 50)
            {
                warnings.Add($"[sl] train-выборка для SL мала ({train.Count}), статистика шумная.");
            }

            if (oos.Count == 0)
            {
                warnings.Add("[sl] OOS-часть для SL пуста (нет сделок после границы по baseline-exit контракту).");
            }

            var trainMetrics = ComputeMetrics(train);
            var oosMetrics = ComputeMetrics(oos);
            var allMetrics = ComputeMetrics(ordered);

            if (oos.Count >= 100 && oosMetrics.Tpr > 0.90 && oosMetrics.Fpr < 0.10)
            {
                errors.Add(
                    $"[sl] OOS TPR={oosMetrics.Tpr:P1}, FPR={oosMetrics.Fpr:P1} при {oos.Count} сделок — подозрение на утечку в SL-слое.");
            }

            if (allMetrics.Samples >= 50 && Math.Abs(allMetrics.Tpr - allMetrics.Fpr) < 0.05)
            {
                warnings.Add(
                    $"[sl] SL-модель почти не отличает high-risk от low-risk: TPR={allMetrics.Tpr:P1}, FPR={allMetrics.Fpr:P1}.");
            }

            int totalPredHigh = ordered.Count(p => p.SlHighDecision);
            if (totalPredHigh == 0)
            {
                warnings.Add("[sl] SlHighDecision никогда не срабатывает — порог риска может быть слишком жёстким.");
            }

            string summary =
                $"[sl] samples={ordered.Count}, train={train.Count}, oos={oos.Count}, excluded={sSplit.Excluded.Count}, " +
                $"TPR_all={allMetrics.Tpr:P1}, FPR_all={allMetrics.Fpr:P1}, " +
                $"TPR_oos={oosMetrics.Tpr:P1}, FPR_oos={oosMetrics.Fpr:P1}";

            var res = new SelfCheckResult
            {
                Success = errors.Count == 0,
                Summary = summary
            };
            res.Errors.AddRange(errors);
            res.Warnings.AddRange(warnings);
            return res;
        }

        private sealed class SlSample
        {
            public DateTime EntryUtc { get; set; }
            public double SlProb { get; set; }
            public bool SlHighDecision { get; set; }
            public bool TrueHighRisk { get; set; }
        }

        private readonly struct SlMetrics
        {
            public SlMetrics(int samples, int pos, int neg, int tp, int fp)
            {
                Samples = samples;
                Pos = pos;
                Neg = neg;
                Tp = tp;
                Fp = fp;

                Tpr = pos > 0 ? (double)Tp / pos : 0.0;
                Fpr = neg > 0 ? (double)Fp / neg : 0.0;
            }

            public int Samples { get; }
            public int Pos { get; }
            public int Neg { get; }
            public int Tp { get; }
            public int Fp { get; }
            public double Tpr { get; }
            public double Fpr { get; }
        }

        private static SlMetrics ComputeMetrics(IReadOnlyList<SlSample> samples)
        {
            if (samples == null || samples.Count == 0)
                return new SlMetrics(0, 0, 0, 0, 0);

            int pos = 0;
            int neg = 0;
            int tp = 0;
            int fp = 0;

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.TrueHighRisk)
                {
                    pos++;
                    if (s.SlHighDecision)
                        tp++;
                }
                else
                {
                    neg++;
                    if (s.SlHighDecision)
                        fp++;
                }
            }

            return new SlMetrics(samples.Count, pos, neg, tp, fp);
        }
    }
}

