namespace SolSignalModel1D_Backtest.Diagnostics
{
    [Flags]
    internal enum LeakageMode
    {
        None = 0,
        IndicatorsShiftForward1Day = 1 << 0,
        WindowingExitShiftForwardMinute = 1 << 1,
        CandlesShiftPricesForward1m = 1 << 2,
        SplitUseEntryDayKey = 1 << 3,
    }

    internal static class LeakageSwitches
    {
        public const LeakageMode Enabled = LeakageMode.None;

        public static bool IsEnabled(LeakageMode mode) => (Enabled & mode) != 0;
    }
}
