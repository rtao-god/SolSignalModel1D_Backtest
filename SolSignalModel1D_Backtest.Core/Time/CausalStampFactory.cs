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
            // 1) Отсекаем weekend на входе и создаём NyTradingEntryUtc (weekend невозможен по типу).
            if (!NyWindowing.TryCreateNyTradingEntryUtc(entryUtc, _nyTz, out var tradingEntryUtc))
            {
                stamp = default;
                return false;
            }

            // 2) Non-morning — это ошибка контракта (не фильтр).
            if (!NyWindowing.IsNyMorning(new EntryUtc(tradingEntryUtc.Value), _nyTz))
            {
                throw new InvalidOperationException(
                    $"[time] Non-morning entryUtc passed where NY-morning expected: {tradingEntryUtc.Value:O}.");
            }

            var nyDay = NyWindowing.GetNyTradingDayOrThrow(tradingEntryUtc, _nyTz);
            var exitUtc = NyWindowing.ComputeBaselineExitUtc(tradingEntryUtc, _nyTz);

            stamp = new CausalStamp(new EntryUtc(tradingEntryUtc.Value), nyDay, exitUtc);
            return true;
        }

        public BaselineExitUtc ComputeBaselineExitUtc(EntryUtc entryUtc)
        {
            return NyWindowing.ComputeBaselineExitUtc(entryUtc, _nyTz);
        }
    }
}
