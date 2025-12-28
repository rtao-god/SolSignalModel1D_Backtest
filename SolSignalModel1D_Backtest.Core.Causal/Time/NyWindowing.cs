using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Diagnostics;
using System;
using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
{
    /// <summary>
    /// NY NyWindowing contract.
    ///
    /// Контракт времени/каузальности:
    /// - входы всегда UTC (EntryUtc);
    /// - каноничные ключи времени:
    ///   EntryUtc (UTC-момент),
    ///   NyTradingEntryUtc (валидированный NY-morning, не выходной),
    ///   BaselineExitUtc (вычисляется из EntryUtc),
    ///   EntryDayKeyUtc (00:00Z дня входа),
    ///   ExitDayKeyUtc (00:00Z дня baseline-exit);
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
    public static partial class NyWindowing
    {
        public static TimeZoneInfo NyTz => TimeZones.NewYork;

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
            if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("utc must be UTC.", nameof(utc));

            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, nyTz);
            return new NyLocalStamp(local);
        }

        private static bool IsNyMorningLocal(DateTime local, TimeZoneInfo nyTz)
        {
            var d = local.Date;

            // DST определяем по полудню даты (стабильно на границах DST).
            var noonLocal = new DateTime(d.Year, d.Month, d.Day, 12, 0, 0, DateTimeKind.Unspecified);
            bool dst = nyTz.IsDaylightSavingTime(noonLocal);
            int expectedHourLocal = dst ? 8 : 7;

            return local.Hour == expectedHourLocal
                   && local.Minute == 0
                   && local.Second == 0
                   && local.Millisecond == 0;
        }

        private static NyLocalStamp EnsureNyTradingEntryUtcOrThrow(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);

            if (stamp.IsWeekend)
                throw new InvalidOperationException($"[time] NyTradingEntryUtc cannot be weekend: {entryUtc.Value:O}.");

            if (!IsNyMorningLocal(stamp.Local, nyTz))
                throw new InvalidOperationException(
                    $"[time] NyTradingEntryUtc must be NY morning (07:00/08:00 local). entryUtc={entryUtc.Value:O}, nyLocal={stamp.Local:O}.");

            return stamp;
        }

        public static bool TryCreateNyTradingEntryUtc(EntryUtc entryUtc, TimeZoneInfo nyTz, out NyTradingEntryUtc tradingEntryUtc)
        {
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (entryUtc.IsDefault) throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);

            if (stamp.IsWeekend || !IsNyMorningLocal(stamp.Local, nyTz))
            {
                tradingEntryUtc = default;
                return false;
            }

            tradingEntryUtc = new NyTradingEntryUtc(entryUtc.Value, default);
            return true;
        }

        public static NyTradingEntryUtc CreateNyTradingEntryUtcOrThrow(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (!TryCreateNyTradingEntryUtc(entryUtc, nyTz, out var tradingEntryUtc))
            {
                var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);

                if (stamp.IsWeekend)
                    throw new InvalidOperationException($"[time] Weekend entry is not allowed for NyTradingEntryUtc: {entryUtc.Value:O}.");

                throw new InvalidOperationException(
                    $"[time] NyTradingEntryUtc requires NY-morning entry (07:00/08:00 local). entryUtc={entryUtc.Value:O}, nyLocal={stamp.Local:O}.");
            }

            return tradingEntryUtc;
        }

        public static bool IsWeekendInNy(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault) throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));
            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            return stamp.IsWeekend;
        }

        public static bool IsNyMorning(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (entryUtc.IsDefault) throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend) return false;

            return IsNyMorningLocal(stamp.Local, nyTz);
        }

        public static bool IsNyMorning(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            var stamp = EnsureNyTradingEntryUtcOrThrow(entryUtc, nyTz);
            return IsNyMorningLocal(stamp.Local, nyTz);
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
            var stamp = EnsureNyTradingEntryUtcOrThrow(entryUtc, nyTz);

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
            var stamp = EnsureNyTradingEntryUtcOrThrow(entryUtc, nyTz);

            return ComputeBaselineExitUtcCore(entryUtc.Value, stamp.Local, nyTz);
        }

        public static bool TryComputeBaselineExitUtc(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz, out BaselineExitUtc exitUtc)
        {
            exitUtc = ComputeBaselineExitUtc(entryUtc, nyTz);
            return true;
        }

        // ===== exit-day-key (baseline-exit -> 00:00Z) =====

        public static ExitDayKeyUtc ComputeExitDayKeyUtc(EntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            if (!TryComputeExitDayKeyUtc(entryUtc, nyTz, out var exitDayKeyUtc))
                throw new InvalidOperationException($"[time] Weekend entry is not allowed: {entryUtc.Value:O}.");

            return exitDayKeyUtc;
        }

        public static bool TryComputeExitDayKeyUtc(EntryUtc entryUtc, TimeZoneInfo nyTz, out ExitDayKeyUtc exitDayKeyUtc)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            var stamp = ConvertOnceUtcToNyLocal(entryUtc.Value, nyTz);
            if (stamp.IsWeekend)
            {
                exitDayKeyUtc = default;
                return false;
            }

            var exitUtcMoment = ComputeBaselineExitUtcMomentCore(entryUtc.Value, stamp.Local, nyTz);
            exitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtcMoment);
            return true;
        }

        public static ExitDayKeyUtc ComputeExitDayKeyUtc(NyTradingEntryUtc entryUtc, TimeZoneInfo nyTz)
        {
            var stamp = EnsureNyTradingEntryUtcOrThrow(entryUtc, nyTz);

            var exitUtcMoment = ComputeBaselineExitUtcMomentCore(entryUtc.Value, stamp.Local, nyTz);
            return ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtcMoment);
        }

        private static BaselineExitUtc ComputeBaselineExitUtcCore(DateTime entryUtc, DateTime entryLocal, TimeZoneInfo nyTz)
        {
            var exitUtcDt = ComputeBaselineExitUtcMomentCore(entryUtc, entryLocal, nyTz);
            return new BaselineExitUtc(new UtcInstant(exitUtcDt));
        }

        private static DateTime ComputeBaselineExitUtcMomentCore(DateTime entryUtc, DateTime entryLocal, TimeZoneInfo nyTz)
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
            if (LeakageSwitches.IsEnabled(LeakageMode.WindowingExitShiftForwardMinute))
                exitLocal = exitLocal.AddMinutes(1);
            var exitUtcDt = TimeZoneInfo.ConvertTimeToUtc(exitLocal, nyTz);

            if (exitUtcDt <= entryUtc)
                throw new InvalidOperationException(
                    $"[time] Invalid baseline window: start={entryUtc:O}, end={exitUtcDt:O}.");

            return exitUtcDt;
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

