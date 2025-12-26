using System;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
{
    /// <summary>
    /// Нормализация entry day-key (00:00Z) в канонический момент входа "NY morning"
    /// через единый time-contract (NyWindowing).
    ///
    /// Инварианты:
    /// - вход: EntryDayKeyUtc (00:00Z);
    /// - выход: EntryUtc, валидный по NyWindowing.IsNyMorning;
    /// - weekend по NY локальному календарю запрещён (кидаем исключение).
    /// </summary>
    public static class NyMorningEntryUtc
    {
        public static EntryUtc FromDayKeyUtcOrThrow(EntryDayKeyUtc dayKeyUtc)
        {
            if (dayKeyUtc.IsDefault)
                throw new ArgumentException("dayKeyUtc must be initialized (non-default).", nameof(dayKeyUtc));

            var dateUtc00 = dayKeyUtc.Value;

            if (dateUtc00.Kind != DateTimeKind.Utc)
                throw new ArgumentException("dayKeyUtc must be UTC.", nameof(dayKeyUtc));

            if (dateUtc00.TimeOfDay != TimeSpan.Zero)
                throw new ArgumentException(
                    $"Expected EntryDayKeyUtc at 00:00Z, got {dateUtc00:O}.",
                    nameof(dayKeyUtc));

            var nyTz = TimeZones.NewYork;

            var noonUtc = dateUtc00.AddHours(12);
            var nyLocalNoon = TimeZoneInfo.ConvertTimeFromUtc(noonUtc, nyTz);

            var nyDay = new NyTradingDay(DateOnly.FromDateTime(nyLocalNoon));

            return NyWindowing.ComputeEntryUtcFromNyDayOrThrow(nyDay, nyTz);
        }

        public static EntryUtc RequireIsNyMorningOrThrow(EntryUtc entryUtc)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var nyTz = TimeZones.NewYork;

            if (!NyWindowing.IsNyMorning(entryUtc, nyTz))
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(entryUtc.Value, nyTz);

                throw new InvalidOperationException(
                    $"Non-morning entryUtc passed where NY-morning expected: {entryUtc.Value:O}. " +
                    $"NY local={local:yyyy-MM-dd HH:mm:ss}, day={local:dddd}.");
            }

            return entryUtc;
        }
    }
}
