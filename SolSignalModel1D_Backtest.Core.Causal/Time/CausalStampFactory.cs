using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
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
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            // Weekend — фильтр (TryCreate=false), non-morning — ошибка контракта (throw).
            if (NyWindowing.IsWeekendInNy(entryUtc, _nyTz))
            {
                stamp = default;
                return false;
            }

            if (!NyWindowing.IsNyMorning(entryUtc, _nyTz))
            {
                var nyLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc.Value, _nyTz);
                throw new InvalidOperationException(
                    $"[time] Non-morning entryUtc passed where NY-morning expected: entryUtc={entryUtc.Value:O}, nyLocal={nyLocal:O}.");
            }

            var tradingEntryUtc = NyWindowing.CreateNyTradingEntryUtcOrThrow(entryUtc, _nyTz);

            var nyDay = NyWindowing.GetNyTradingDayOrThrow(tradingEntryUtc, _nyTz);
            var exitUtc = NyWindowing.ComputeBaselineExitUtc(tradingEntryUtc, _nyTz);

            stamp = new CausalStamp(tradingEntryUtc.AsEntryUtc(), nyDay, exitUtc);
            return true;
        }

        public BaselineExitUtc ComputeBaselineExitUtc(EntryUtc entryUtc)
        {
            return NyWindowing.ComputeBaselineExitUtc(entryUtc, _nyTz);
        }
    }
}

