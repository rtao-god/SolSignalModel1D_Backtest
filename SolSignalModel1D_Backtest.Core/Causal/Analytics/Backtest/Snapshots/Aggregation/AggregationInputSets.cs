using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation
{
    /// <summary>
    /// Готовые наборы для агрегационных снапшотов.
    /// Контракт:
    /// - Eligible — только валидные дни (без исключённых).
    /// - Excluded — дни, которые исключены апстримом (например, weekend-entry без baseline-exit).
    /// - Train/OOS — это split только для Eligible.
    /// - Split/фильтрация выполняются один раз в раннере/дирижёре.
    /// </summary>
    public sealed class AggregationInputSets
    {
        public required TrainBoundaryMeta Boundary { get; init; }

        public required IReadOnlyList<BacktestAggRow> Eligible { get; init; }
        public required IReadOnlyList<BacktestAggRow> Excluded { get; init; }

        public required IReadOnlyList<BacktestAggRow> Train { get; init; }
        public required IReadOnlyList<BacktestAggRow> Oos { get; init; }
    }
}
