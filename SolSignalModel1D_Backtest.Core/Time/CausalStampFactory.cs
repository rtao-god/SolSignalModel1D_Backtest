using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    public sealed class CausalStampFactory
    {
        private readonly TimeZoneInfo _nyTz;

        public CausalStampFactory(TimeZoneInfo nyTz)
        {
            _nyTz = nyTz ?? throw new ArgumentNullException(nameof(nyTz));
        }

        public bool TryCreate(EntryUtc entryUtc, out CausalStamp stamp)
        {
            var nyLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc.Value, _nyTz);

            if (nyLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                stamp = default;
                return false;
            }

            var nyDay = new NyTradingDay(DateOnly.FromDateTime(nyLocal));
            var exitUtc = ComputeBaselineExitUtc(entryUtc);

            stamp = new CausalStamp(entryUtc, nyDay, exitUtc);
            return true;
        }

        public BaselineExitUtc ComputeBaselineExitUtc(EntryUtc entryUtc)
        {
            // Контракт baseline-exit — единое место. Реализация NY/DST — здесь.
            // Внешний код не пересчитывает окна руками.

            // В реальном коде: точное правило 07/08 + -2min и т.д., но строго внутри этого типа.
            throw new NotImplementedException("Implement NY baseline-exit contract here.");
        }
    }
}
