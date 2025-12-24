using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// Фабрика causal stamp для NY-дневного контракта.
    /// Контракт:
    /// - weekend entry по NY локальному времени => stamp не создаётся (TryCreate=false);
    /// - non-morning entry (не 07/08 NY local) => ошибка контракта (throw).
    /// </summary>
    public sealed class CausalStampFactory
    {
        private readonly TimeZoneInfo _nyTz;

        public CausalStampFactory(TimeZoneInfo nyTz)
        {
            _nyTz = nyTz ?? throw new ArgumentNullException(nameof(nyTz));
        }

        public bool TryCreate(EntryUtc entryUtc, out CausalStamp stamp)
        {
            if (NyWindowing.IsWeekendInNy(entryUtc, _nyTz))
            {
                stamp = default;
                return false;
            }

            if (!NyWindowing.IsNyMorning(entryUtc, _nyTz))
            {
                throw new InvalidOperationException(
                    $"[time] Non-morning entryUtc passed where NY-morning expected: {entryUtc.Value:O}.");
            }

            var nyDay = NyWindowing.GetNyTradingDayOrThrow(entryUtc, _nyTz);
            var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, _nyTz);

            stamp = new CausalStamp(entryUtc, nyDay, exitUtc);
            return true;
        }

        public BaselineExitUtc ComputeBaselineExitUtc(EntryUtc entryUtc)
        {
            return NyWindowing.ComputeBaselineExitUtc(entryUtc, _nyTz);
        }
    }
}
