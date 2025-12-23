using System;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
{
    public interface IHasCausalStamp
    {
        CausalStamp Stamp { get; }
    }

    /// <summary>
    /// Каузальная запись. Не содержит forward/outcomes.
    /// </summary>
    public sealed class CausalRow : IHasCausalStamp
    {
        public CausalStamp Stamp { get; }

        // Пример каузальных полей (здесь только то, что доступно на момент EntryUtc). (переписать на настоящие)
        public double? Fng { get; init; }
        public double? DxyChg30 { get; init; }
        public double? GoldChg30 { get; init; }

        public double? SolRet1 { get; init; }
        public double? SolRet3 { get; init; }
        public double? SolRet30 { get; init; }

        public bool? IsMorning { get; init; }

        public bool RegimeDown { get; init; }

        public CausalRow(CausalStamp stamp)
        {
            Stamp = stamp;
        }
    }
}
