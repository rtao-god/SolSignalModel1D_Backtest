using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Time
{
    /// <summary>
    /// Фильтры свечей для выделения NY-окон.
    ///
    /// Важно:
    /// - DST не вычисляем вручную: TimeZoneInfo.ConvertTimeFromUtc уже даёт корректный локальный час;
    /// - фильтрация идёт по NY-локальному календарю (weekend исключаем).
    /// </summary>
    public static class NyCandleWindowFilters
    {
        public static List<Candle6h> FilterNyTrainWindows(List<Candle6h> all, TimeZoneInfo nyTz)
        {
            if (all == null) throw new ArgumentNullException(nameof(all));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var res = new List<Candle6h>();

            foreach (var c in all)
            {
                var ny = TimeZoneInfo.ConvertTimeFromUtc(c.OpenTimeUtc, nyTz);
                if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                // - "утро": 07 или 08 local (зима/лето),
                // - "день": 13 или 14 local (зима/лето).
                if (ny.Hour is 7 or 8 or 13 or 14)
                    res.Add(c);
            }

            return res.OrderBy(c => c.OpenTimeUtc).ToList();
        }

        public static List<Candle6h> FilterNyMorningOnly(List<Candle6h> all, TimeZoneInfo nyTz)
        {
            if (all == null) throw new ArgumentNullException(nameof(all));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            var res = new List<Candle6h>();

            foreach (var c in all)
            {
                var ny = TimeZoneInfo.ConvertTimeFromUtc(c.OpenTimeUtc, nyTz);
                if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                if (ny.Hour is 7 or 8)
                    res.Add(c);
            }

            return res.OrderBy(c => c.OpenTimeUtc).ToList();
        }
    }
}
