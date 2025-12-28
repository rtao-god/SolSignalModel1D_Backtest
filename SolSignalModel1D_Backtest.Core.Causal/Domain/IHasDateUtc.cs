namespace SolSignalModel1D_Backtest.Core.Causal.Domain
{
    /// <summary>
    /// Минимальный контракт: объект имеет дату в UTC.
    ///
    /// Контракт:
    /// - DateUtc должен быть "днём" (обычно 00:00:00Z) либо согласованным UTC-моментом,
    ///   который используется как ключ для группировок/сэмплинга.
    /// - Kind ожидается Utc (или строго согласованная договорённость по месту вызова).
    /// </summary>
    public interface IHasDateUtc
    {
        DateTime DateUtc { get; }
    }
}
