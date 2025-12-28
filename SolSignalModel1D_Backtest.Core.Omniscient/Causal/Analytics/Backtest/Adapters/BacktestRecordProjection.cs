using System;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Causal.Analytics.Backtest.Adapters
{
    public static class BacktestRecordProjection
    {
        public static BacktestAggRow ToAggRow(this BacktestRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Causal == null) throw new InvalidOperationException("[proj] BacktestRecord.Causal is null.");
            if (r.Forward == null) throw new InvalidOperationException("[proj] BacktestRecord.Forward is null.");

            var entryUtcInstant = r.Causal.EntryUtc.Value;

            if (r.PredMicroUp && r.PredMicroDown)
            {
                throw new InvalidOperationException(
                    $"[proj] Invalid micro prediction flags: both PredMicroUp and PredMicroDown are true for {entryUtcInstant:O}.");
            }

            if (r.FactMicroUp && r.FactMicroDown)
            {
                throw new InvalidOperationException(
                    $"[proj] Invalid micro fact flags: both FactMicroUp and FactMicroDown are true for {entryUtcInstant:O}.");
            }

            if (r.SlProb is not double slProb)
            {
                throw new InvalidOperationException(
                    $"[proj] SlProb is null for {entryUtcInstant:O}. " +
                    "Это означает, что SL-слой не был посчитан до стадии агрегационной аналитики (pipeline bug).");
            }

            if (r.SlHighDecision is not bool slHighDecision)
            {
                throw new InvalidOperationException(
                    $"[proj] SlHighDecision is null for {entryUtcInstant:O}. " +
                    "Это означает, что SL-слой не был посчитан до стадии агрегационной аналитики (pipeline bug).");
            }

            var entryDayKeyUtc = r.TradingEntryUtc.EntryDayKeyUtc;

            ValidateProbTriOrThrow(
                tag: "Day",
                entryUtcInstant: entryUtcInstant,
                up: r.ProbUp_Day,
                flat: r.ProbFlat_Day,
                down: r.ProbDown_Day);

            ValidateProbTriOrThrow(
                tag: "Day+Micro",
                entryUtcInstant: entryUtcInstant,
                up: r.ProbUp_DayMicro,
                flat: r.ProbFlat_DayMicro,
                down: r.ProbDown_DayMicro);

            ValidateProbTriOrThrow(
                tag: "Total",
                entryUtcInstant: entryUtcInstant,
                up: r.ProbUp_Total,
                flat: r.ProbFlat_Total,
                down: r.ProbDown_Total);

            return new BacktestAggRow
            {
                EntryDayKeyUtc = entryDayKeyUtc,
                TrueLabel = r.TrueLabel,

                PredLabel_Day = r.PredLabel_Day,
                PredLabel_DayMicro = r.PredLabel_DayMicro,
                PredLabel_Total = r.PredLabel_Total,

                ProbUp_Day = r.ProbUp_Day,
                ProbFlat_Day = r.ProbFlat_Day,
                ProbDown_Day = r.ProbDown_Day,

                ProbUp_DayMicro = r.ProbUp_DayMicro,
                ProbFlat_DayMicro = r.ProbFlat_DayMicro,
                ProbDown_DayMicro = r.ProbDown_DayMicro,

                ProbUp_Total = r.ProbUp_Total,
                ProbFlat_Total = r.ProbFlat_Total,
                ProbDown_Total = r.ProbDown_Total,

                Conf_Day = r.Conf_Day,
                Conf_Micro = r.Conf_Micro,

                SlProb = slProb,
                SlHighDecision = slHighDecision,

                PredMicroUp = r.PredMicroUp,
                PredMicroDown = r.PredMicroDown,
                FactMicroUp = r.FactMicroUp,
                FactMicroDown = r.FactMicroDown
            };
        }

        private static void ValidateProbTriOrThrow(string tag, DateTime entryUtcInstant, double up, double flat, double down)
        {
            if (!double.IsFinite(up) || !double.IsFinite(flat) || !double.IsFinite(down))
            {
                throw new InvalidOperationException(
                    $"[proj] Non-finite probabilities in layer '{tag}' for {entryUtcInstant:O}. " +
                    $"P_up={up}, P_flat={flat}, P_down={down}.");
            }

            if (up < 0.0 || flat < 0.0 || down < 0.0)
            {
                throw new InvalidOperationException(
                    $"[proj] Negative probabilities in layer '{tag}' for {entryUtcInstant:O}. " +
                    $"P_up={up}, P_flat={flat}, P_down={down}.");
            }

            double sum = up + flat + down;
            const double eps = 1e-9;

            if (sum <= eps)
            {
                throw new InvalidOperationException(
                    $"[proj] Degenerate probability triple (sum<=0) in layer '{tag}' for {entryUtcInstant:O}. " +
                    $"P_up={up}, P_flat={flat}, P_down={down}.");
            }

            if (Math.Abs(sum - 1.0) > 1e-6)
            {
                throw new InvalidOperationException(
                    $"[proj] Probability sum != 1 in layer '{tag}' for {entryUtcInstant:O}. " +
                    $"sum={sum}, P_up={up}, P_flat={flat}, P_down={down}.");
            }
        }
    }
}
