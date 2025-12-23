namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
{
    public sealed class OmniRecord
    {
        public CausalRow Causal { get; init; } = null!;
        public ForwardOutcome Forward { get; init; } = null!;
    }

    public sealed class ForwardOutcome
    {
        // Всё, что “из будущего”: TP/SL, пути, close, pnl, etc.
    }
}
