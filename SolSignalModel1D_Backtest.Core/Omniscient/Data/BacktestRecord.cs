using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
{
    /// <summary>
    /// Запись бэктеста за один день: каузальная часть + forward-факты рынка.
    /// </summary>
    public sealed class BacktestRecord
    {
        public required CausalPredictionRecord Causal { get; init; }
        public required ForwardOutcomes Forward { get; init; }

        // ===== Truth =====
        public int TrueLabel => Forward.TrueLabel;
        public bool FactMicroUp => Forward.FactMicroUp;
        public bool FactMicroDown => Forward.FactMicroDown;

        // ===== Pred labels =====
        public int PredLabel => Causal.PredLabel;
        public int PredLabel_Day => Causal.PredLabel_Day;
        public int PredLabel_DayMicro => Causal.PredLabel_DayMicro;
        public int PredLabel_Total => Causal.PredLabel_Total;

        // ===== Probabilities =====
        public double ProbUp_Day => Causal.ProbUp_Day;
        public double ProbFlat_Day => Causal.ProbFlat_Day;
        public double ProbDown_Day => Causal.ProbDown_Day;

        public double ProbUp_DayMicro => Causal.ProbUp_DayMicro;
        public double ProbFlat_DayMicro => Causal.ProbFlat_DayMicro;
        public double ProbDown_DayMicro => Causal.ProbDown_DayMicro;

        public double ProbUp_Total => Causal.ProbUp_Total;
        public double ProbFlat_Total => Causal.ProbFlat_Total;
        public double ProbDown_Total => Causal.ProbDown_Total;

        public double Conf_Day => Causal.Conf_Day;
        public double Conf_Micro => Causal.Conf_Micro;

        // ===== Micro prediction flags =====
        public bool MicroPredicted => Causal.MicroPredicted;
        public bool PredMicroUp => Causal.PredMicroUp;
        public bool PredMicroDown => Causal.PredMicroDown;

        // ===== Regime/context =====
        public bool RegimeDown => Causal.RegimeDown;
        public string Reason => Causal.Reason;
        public double MinMove => Causal.MinMove;

        // ===== Forward window facts =====
        public double Entry => Forward.Entry;
        public double MaxHigh24 => Forward.MaxHigh24;
        public double MinLow24 => Forward.MinLow24;
        public double Close24 => Forward.Close24;
        public double ForwardMinMove => Forward.MinMove;
        public DateTime WindowEndUtc => Forward.WindowEndUtc;
        public IReadOnlyList<Candle1m> DayMinutes => Forward.DayMinutes;

        // ===== SL runtime =====
        public double? SlProb => Causal.SlProb;
        public bool? SlHighDecision => Causal.SlHighDecision;
        public double? Conf_SlLong => Causal.Conf_SlLong;
        public double? Conf_SlShort => Causal.Conf_SlShort;

        // ===== Delayed runtime (каузальная часть) =====
        public string? DelayedSource => Causal.DelayedSource;
        public bool? DelayedEntryAsked => Causal.DelayedEntryAsked;
        public bool? DelayedEntryUsed => Causal.DelayedEntryUsed;
        public double? DelayedIntradayTpPct => Causal.DelayedIntradayTpPct;
        public double? DelayedIntradaySlPct => Causal.DelayedIntradaySlPct;
        public int? TargetLevelClass => Causal.TargetLevelClass;

        // ===== Time keys (strong-typed) =====
        public EntryUtc EntryUtc => Forward.EntryUtc;
        public EntryDayKeyUtc EntryDayKeyUtc => Forward.EntryDayKeyUtc;

        public void EnsureEntryUtcCoherenceOrThrow()
        {
            var forward = EntryUtc.Value;
            var causal = TradingEntryUtc.Value;

            if (forward != causal)
                throw new InvalidOperationException(
                    $"[BacktestRecord] EntryUtc mismatch: Forward.EntryUtc={forward:O} vs Causal.TradingEntryUtc={causal:O}");
        }

        // Back-compat (temporary): prefer EntryDayKeyUtc.
        [Obsolete("Use EntryDayKeyUtc (explicit entry day-key).", error: false)]
        public EntryDayKeyUtc DayKeyUtc => EntryDayKeyUtc;

        // Causal boundary: trading entry (weekend impossible by type).
        public NyTradingEntryUtc TradingEntryUtc => Causal.EntryUtc;

        // ===== Omniscient execution facts =====
        public DelayedExecutionFacts? DelayedExecution { get; set; }

        public bool DelayedEntryExecuted => DelayedExecution is not null;
        public double? DelayedEntryPrice => DelayedExecution?.EntryPrice;
        public DateTime? DelayedEntryExecutedAtUtc => DelayedExecution?.ExecutedAtUtc;
        public DelayedIntradayResult? DelayedIntradayResult => DelayedExecution?.IntradayResult;

        public bool AntiDirectionApplied { get; set; }
    }
}
