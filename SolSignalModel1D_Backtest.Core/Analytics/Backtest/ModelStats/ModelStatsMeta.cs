using System;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.ModelStats
{
    /// <summary>
    /// Общие метаданные прогона модельных статистик.
    /// Это "шапка" отчёта/снапшота, чтобы принтеры и API не пересчитывали контекст.
    /// </summary>
    public sealed class ModelStatsMeta
    {
        /// <summary>
        /// Режим запуска (консольная аналитика/и т.п.).
        /// </summary>
        public ModelRunKind RunKind { get; set; } = ModelRunKind.Unknown;

        /// <summary>
        /// Граница train-периода: exit-day-key (UTC 00:00).
        /// </summary>
        public DayKeyUtc TrainUntilExitDayKeyUtc { get; set; }

        /// <summary>
        /// ISO-дата границы (yyyy-MM-dd) для печати/репортов (без пересчёта культуры/формата).
        /// </summary>
        public string TrainUntilIsoDate { get; set; } = string.Empty;

        /// <summary>
        /// Размер окна recent (в днях).
        /// </summary>
        public int RecentDays { get; set; }

        /// <summary>
        /// Есть ли реально OOS-сегмент (oosCount > 0).
        /// </summary>
        public bool HasOos { get; set; }

        public int TrainRecordsCount { get; set; }
        public int OosRecordsCount { get; set; }
        public int TotalRecordsCount { get; set; }
        public int RecentRecordsCount { get; set; }
    }
}
