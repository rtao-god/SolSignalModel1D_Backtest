using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Infra
{
    /// <summary>
    /// Единая точка получения TimeZoneInfo для проекта.
    ///
    /// Контракт:
    /// - Идентификаторы таймзон различаются между Windows и Linux/macOS.
    /// - Резолв делается один раз (Lazy) и затем используется как глобальная константа.
    /// </summary>
    public static class TimeZones
    {
        // Ленивый резолв: таймзона должна быть стабильной и не создаваться многократно.
        private static readonly Lazy<TimeZoneInfo> _newYork = new Lazy<TimeZoneInfo>(ResolveNewYork);

        /// <summary>
        /// Нью-Йорк (America/New_York). Используется для DST-логики утреннего бара и baseline-окна.
        /// </summary>
        public static TimeZoneInfo NewYork => _newYork.Value;

        private static TimeZoneInfo ResolveNewYork()
        {
            // 1) Попытка Windows ID.
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                // Игнорируем: на Linux/macOS этот ID обычно отсутствует.
            }

            // 2) Попытка IANA ID (Linux/macOS).
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch (Exception ex)
            {
                // 3) Фатальная ошибка: без этой TZ вся логика окон становится некорректной.
                throw new InvalidOperationException(
                    "[tz] Cannot resolve New York timezone. " +
                    "Try installing tzdata (Linux) or ensure system time zones are available.",
                    ex);
            }
        }
    }
}
