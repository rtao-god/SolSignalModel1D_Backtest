using SolSignalModel1D_Backtest.Core.Infra;
using System;

namespace SolSignalModel1D_Backtest.Core.Time
{
    /// <summary>
    /// NY NyWindowing contract.
    ///
    /// Контракт времени/каузальности:
    /// - входы всегда UTC (EntryUtc);
    /// - weekend по NY локальному времени запрещён для causal:
    ///   Try* возвращает false, OrThrow кидает исключение;
    /// - "NY morning" определяется по NY-локальному времени:
    ///   07:00 зимой / 08:00 летом (DST);
    /// - baseline-exit = следующее NY-утро минус 2 минуты;
    /// - Friday entry: baseline-exit переносится на понедельник (addDays=3).
    ///
    /// Тонкости DST:
    /// - DST определяется по локальному "полудню" целевой даты, чтобы избежать пограничных
    ///   эффектов при ночных DST-переходах.
    /// - Локальные DateTime создаются как Unspecified и трактуются как время nyTz.
    /// </summary>
    public static class NyWindowing
    {
        /// <summary>Единый источник таймзоны Нью-Йорка.</summary>
        public static TimeZoneInfo NyTz => TimeZones.NewYork;

        // ===== "convert once" helper =====
        private readonly struct NyLocalStamp
        {
            public readonly DateTime Local;
            public readonly DayOfWeek DayOfWeek;
            public readonly bool IsWeekend;

            public NyLocalStamp(DateTime local)
            {
                Local = local;
                DayOfWeek = local.DayOfWeek;
                IsWeekend = DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            }
        }

        private static NyLocalStamp ConvertOnceUtcToNyLocal(DateTime utc, TimeZoneInfo nyTz)
        {
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (utc == default) throw new ArgumentException("utc must be non-default.", nameof(utc));
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("utc must be UTC.", nameof(utc));

            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, nyTz);
            return new NyLocalStamp(local);
        }

        // ===== NyTradingEntryUtc factories (единственный путь создать тип) =====

        public static bool TryCreateNyTradingEntryUtc(EntryUtc entryUtc, TimeZoneInfo nyTz, out NyTradingEntryUtc tradingEntryUtc)
        {
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
            {
                tradingEntryUtc = default;
                return false;
            }

            // ВАЖНО: NyTradingEntryUtc ожидает DateTime (см. CS1503: UtcInstant -> DateTime).
            tradingEntryUtc = new NyTradingEntryUtc(entryUtc.Value);
            return true;
        }

        public static NyTradingEntryUtc CreateNyTradingEntryUtcOrThrow(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (!TryCreateNyTradingEntryUtc(entryUtc, nyTz, out var tradingEntryUtc))
                throw new InvalidOperationException($"[time] Weekend entry is not allowed for NyTradingEntryUtc: {entryUtc.Value:O}.");

            return tradingEntryUtc;
        }

        // ===== Weekend / trading-day helpers =====

        public static bool IsWeekendInNy(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            return stamp.IsWeekend;
        }

        public static bool IsNyMorning(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
                return false;

            int expectedHourLocal = nyTz.IsDaylightSavingTime(stamp.Local) ? 8 : 7;

            return stamp.Local.Hour == expectedHourLocal
                   && stamp.Local.Minute == 0
                   && stamp.Local.Second == 0
                   && stamp.Local.Millisecond == 0;
        }

        public static bool IsNyMorning(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);

            // Инвариант типа: weekend недопустим. При нарушении — fail-fast.
            if (stamp.IsWeekend)
                throw new InvalidOperationException($"[time] NyTradingEntryUtc cannot be weekend: {entryUtc.Value:O}.");

            int expectedHourLocal = nyTz.IsDaylightSavingTime(stamp.Local) ? 8 : 7;

            return stamp.Local.Hour == expectedHourLocal
                   && stamp.Local.Minute == 0
                   && stamp.Local.Second == 0
                   && stamp.Local.Millisecond == 0;
        }

        public static NyTradingDay GetNyTradingDayOrThrow(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
                throw new InvalidOperationException($"[time] Weekend entry is not allowed for causal stamp: {entryUtc.Value:O}.");

            return new NyTradingDay(DateOnly.FromDateTime(stamp.Local));
        }

        public static NyTradingDay GetNyTradingDayOrThrow(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
                throw new InvalidOperationException($"[time] NyTradingEntryUtc cannot be weekend: {entryUtc.Value:O}.");

            return new NyTradingDay(DateOnly.FromDateTime(stamp.Local));
        }

        public static bool TryGetNyTradingDay(EntryUtc entryUtc, TimeZoneInfo nyTz, out NyTradingDay nyDay)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
            {
                nyDay = default;
                return false;
            }

            nyDay = new NyTradingDay(DateOnly.FromDateTime(stamp.Local));
            return true;
        }

        public static bool TryGetNyTradingDay(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz, out NyTradingDay nyDay)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            // По типу weekend недопустим; при нарушении — fail-fast внутри GetNyTradingDayOrThrow.
            nyDay = GetNyTradingDayOrThrow(entryUtc, nyTz);
            return true;
        }

        // ===== baseline-exit =====

        public static BaselineExitUtc ComputeBaselineExitUtc(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (!TryComputeBaselineExitUtc(entryUtc, nyTz, out var exitUtc))
                throw new InvalidOperationException($"[time] Weekend entry is not allowed: {entryUtc.Value:O}.");

            return exitUtc;
        }

        public static bool TryComputeBaselineExitUtc(EntryUtc entryUtc, TimeZoneInfo nyTz, out BaselineExitUtc exitUtc)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
            {
                exitUtc = default;
                return false;
            }

            exitUtc = ComputeBaselineExitUtcCore(entryUtc.Value, stamp.Local, nyTz);
            return true;
        }

        public static BaselineExitUtc ComputeBaselineExitUtc(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
                throw new InvalidOperationException($"[time] NyTradingEntryUtc cannot be weekend: {entryUtc.Value:O}.");

            return ComputeBaselineExitUtcCore(entryUtc.Value, stamp.Local, nyTz);
        }

        public static bool TryComputeBaselineExitUtc(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz, out BaselineExitUtc exitUtc)
        {
            exitUtc = ComputeBaselineExitUtc(entryUtc, nyTz);
            return true;
        }

        private static BaselineExitUtc ComputeBaselineExitUtcCore(DateTime entryUtc, DateTime entryLocal, TimeZoneInfo nyTz)
        {
            int addDays = entryLocal.DayOfWeek == DayOfWeek.Friday ? 3 : 1;

            var targetDateLocal = entryLocal.Date.AddDays(addDays);

            var noonLocal = new DateTime(
                targetDateLocal.Year, targetDateLocal.Month, targetDateLocal.Day,
                12, 0, 0,
                DateTimeKind.Unspecified);

            bool dst = nyTz.IsDaylightSavingTime(noonLocal);
            int morningHourLocal = dst ? 8 : 7;

            var nextMorningLocal = new DateTime(
                targetDateLocal.Year, targetDateLocal.Month, targetDateLocal.Day,
                morningHourLocal, 0, 0,
                DateTimeKind.Unspecified);

            var exitLocal = nextMorningLocal.AddMinutes(-2);
            var exitUtcDt = TimeZoneInfo.ConvertTimeToUtc(exitLocal, nyTz);

            if (exitUtcDt <= entryUtc)
                throw new InvalidOperationException(
                    $"[time] Invalid baseline window: start={entryUtc:O}, end={exitUtcDt:O}.");

            return new BaselineExitUtc(new UtcInstant(exitUtcDt));
        }

        // ===== reverse mapping =====

        public static EntryUtc ComputeEntryUtcFromNyDayOrThrow(NyTradingDay nyDay, TimeZoneInfo nyTz)
        {
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (nyDay.IsDefault) throw new ArgumentException("nyDay must be initialized (non-default).", nameof(nyDay));

            var dayOfWeek = nyDay.Value.DayOfWeek;
            if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                throw new InvalidOperationException($"[time] NyTradingDay cannot be weekend: {nyDay}.");

            var noonLocal = new DateTime(
                nyDay.Value.Year, nyDay.Value.Month, nyDay.Value.Day,
                12, 0, 0,
                DateTimeKind.Unspecified);

            bool dst = nyTz.IsDaylightSavingTime(noonLocal);
            int morningHourLocal = dst ? 8 : 7;

            var morningLocal = new DateTime(
                nyDay.Value.Year, nyDay.Value.Month, nyDay.Value.Day,
                morningHourLocal, 0, 0,
                DateTimeKind.Unspecified);

            var entryUtcDt = TimeZoneInfo.ConvertTimeToUtc(morningLocal, nyTz);
            return new EntryUtc(entryUtcDt);
        }
    }
}
