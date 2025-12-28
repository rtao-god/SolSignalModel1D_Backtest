using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Domain;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
{
    /// <summary>
    /// Контейнер, объединяющий каузальные фичи и рассчитанные forward-исходы для одного дня.
    /// Используется для аналитики, метрик и построения бэктестовых рекордов.
    /// </summary>
    public sealed class OmniscientDataRow(CausalDataRow causal, ForwardOutcomes outcomes) : IHasDateUtc
    {
        public CausalDataRow Causal { get; } = causal ?? throw new ArgumentNullException(nameof(causal));
        public ForwardOutcomes Outcomes { get; } = outcomes ?? throw new ArgumentNullException(nameof(outcomes));

        public DateTime DateUtc => Causal.EntryDayKeyUtc.Value;
    }
}
