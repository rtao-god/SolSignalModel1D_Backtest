using System;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts
{
    /// <summary>
    /// Минимальный контракт для аналитики агрегации/метрик:
    /// - содержит только то, что нужно для расчёта и печати;
    /// - не тянет forward-путь/1m и прочие омнисциентные данные;
    /// - служит "жёсткой границей": принтеры и билдёры видят только это.
    /// </summary>
    public sealed class BacktestAggRow
    {
        /// <summary>
        /// UTC day-key (00:00:00Z). Это НЕ timestamp входа/выхода.
        /// </summary>
        public required DayKeyUtc DayUtc { get; init; }

        /// <summary>
        /// Истинная метка: 0=down, 1=flat, 2=up.
        /// </summary>
        public required int TrueLabel { get; init; }

        public required int PredLabel_Day { get; init; }
        public required int PredLabel_DayMicro { get; init; }
        public required int PredLabel_Total { get; init; }

        public required double ProbUp_Day { get; init; }
        public required double ProbFlat_Day { get; init; }
        public required double ProbDown_Day { get; init; }

        public required double ProbUp_DayMicro { get; init; }
        public required double ProbFlat_DayMicro { get; init; }
        public required double ProbDown_DayMicro { get; init; }

        public required double ProbUp_Total { get; init; }
        public required double ProbFlat_Total { get; init; }
        public required double ProbDown_Total { get; init; }

        /// <summary>
        /// Конфиденсы/веса слоёв (как они считаются — не важно, важно что они уже рассчитаны).
        /// </summary>
        public required double Conf_Day { get; init; }
        public required double Conf_Micro { get; init; }

        /// <summary>
        /// SL-скоры/решения, чтобы понимать, "использовался ли" SL слой.
        /// </summary>
        public required double SlProb { get; init; }
        public required bool SlHighDecision { get; init; }

        /// <summary>
        /// Микро-направления (используются для MicroStats и для direction-логики).
        /// Инвариант: одновременно true быть не должно.
        /// </summary>
        public required bool PredMicroUp { get; init; }
        public required bool PredMicroDown { get; init; }

        /// <summary>
        /// Ground-truth микро-направления (если применимо).
        /// Инвариант: одновременно true быть не должно.
        /// </summary>
        public required bool FactMicroUp { get; init; }
        public required bool FactMicroDown { get; init; }
    }
}
