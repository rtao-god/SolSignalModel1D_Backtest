using System;

namespace SolSignalModel1D_Backtest.Core.Utils.Time
{
    /// <summary>
    /// Нормализация времени к "каузальной дате" (UTC day).
    ///
    /// Контракт:
    /// - Kind=Utc -> берём UTC-день;
    /// - Kind=Unspecified -> трактуем как уже-UTC (типичный результат .Date / парсинга без TZ);
    /// - Kind=Local -> запрещено: локальные даты дают тихие сдвиги и ломают границы Train/OOS.
    /// </summary>
    public static class DateTimeCausalExtensions
    {
        public static DateTime ToCausalDateUtc(this DateTime dt)
        {
            // Запрет Local: это неустранимый источник недетерминизма и "плавающих" границ.
            if (dt.Kind == DateTimeKind.Local)
            {
                throw new InvalidOperationException(
                    $"[time] Local DateTime is запрещён для каузальной даты: {dt:O}. " +
                    "Используй UTC и нормализуй явно.");
            }

            // Нормализация к календарному дню (00:00:00Z). Kind фиксируем как Utc.
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}
