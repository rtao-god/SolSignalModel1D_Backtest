using System;
using SolSignalModel1D_Backtest.Core.Time;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing.ComputeBaselineExitUtc
{
    internal static class ComputeBaselineExitUtc
    {
        public static DateTime ForEntryUtc(DateTime entryUtc, TimeSpan? nyMorningLocalTime = null)
        {
            if (entryUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Expected UTC DateTime.", nameof(entryUtc));

            if (nyMorningLocalTime != null)
                throw new InvalidOperationException(
                    "[tests] nyMorningLocalTime override is not supported. " +
                    "Use Core.Time.NyWindowing contract (DST-aware 07/08).");

            var baselineExitUtc = CoreNyWindowing.ComputeBaselineExitUtc(new EntryUtc(entryUtc), CoreNyWindowing.NyTz);
            return baselineExitUtc.Value.AddMinutes(2);
        }
    }
}
