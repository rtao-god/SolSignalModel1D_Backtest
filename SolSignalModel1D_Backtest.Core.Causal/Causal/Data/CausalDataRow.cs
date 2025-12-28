using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
{
    public sealed class CausalDataRow
    {
        public EntryUtc EntryUtc { get; }
        public NyTradingEntryUtc TradingEntryUtc { get; }

        public EntryDayKeyUtc EntryDayKeyUtc => EntryDayKeyUtc.FromUtcMomentOrThrow(EntryUtc.Value);

        public bool RegimeDown { get; }
        public bool IsMorning { get; }
        public int HardRegime { get; }

        public double MinMove { get; }

        public double SolRet30 { get; }
        public double BtcRet30 { get; }
        public double SolBtcRet30 { get; }

        public double SolRet1 { get; }
        public double SolRet3 { get; }
        public double BtcRet1 { get; }
        public double BtcRet3 { get; }

        public double FngNorm { get; }
        public double DxyChg30 { get; }
        public double GoldChg30 { get; }

        public double BtcVs200 { get; }

        public double SolRsiCenteredScaled { get; }
        public double RsiSlope3Scaled { get; }

        public double GapBtcSol1 { get; }
        public double GapBtcSol3 { get; }

        public double AtrPct { get; }
        public double DynVol { get; }

        public double SolAboveEma50 { get; }
        public double SolEma50vs200 { get; }
        public double BtcEma50vs200 { get; }

        public ReadOnlyMemory<double> FeaturesVector => _featuresVector;
        private readonly double[] _featuresVector;

        public static IReadOnlyList<string> FeatureNames { get; } = new[]
        {
            nameof(SolRet30),
            nameof(BtcRet30),
            nameof(SolBtcRet30),

            nameof(SolRet1),
            nameof(SolRet3),
            nameof(BtcRet1),
            nameof(BtcRet3),

            nameof(FngNorm),
            nameof(DxyChg30),
            nameof(GoldChg30),

            nameof(BtcVs200),

            nameof(SolRsiCenteredScaled),
            nameof(RsiSlope3Scaled),

            nameof(GapBtcSol1),
            nameof(GapBtcSol3),

            "RegimeDownFlag",
            nameof(AtrPct),
            nameof(DynVol),
            "HardRegimeIs2Flag",

            nameof(SolAboveEma50),
            nameof(SolEma50vs200),
            nameof(BtcEma50vs200),
        };

        public static int FeatureCount => FeatureNames.Count;

        public CausalDataRow(
            NyTradingEntryUtc entryUtc,
            bool regimeDown,
            bool isMorning,
            int hardRegime,
            double minMove,

            double solRet30,
            double btcRet30,
            double solBtcRet30,

            double solRet1,
            double solRet3,
            double btcRet1,
            double btcRet3,

            double fngNorm,
            double dxyChg30,
            double goldChg30,

            double btcVs200,

            double solRsiCenteredScaled,
            double rsiSlope3Scaled,

            double gapBtcSol1,
            double gapBtcSol3,

            double atrPct,
            double dynVol,

            double solAboveEma50,
            double solEma50vs200,
            double btcEma50vs200)
        {
            if (entryUtc.IsDefault)
                throw new ArgumentException("entryUtc must be initialized (non-default).", nameof(entryUtc));

            TradingEntryUtc = entryUtc;
            EntryUtc = entryUtc.AsEntryUtc();

            RegimeDown = regimeDown;
            IsMorning = isMorning;
            HardRegime = hardRegime;

            MinMove = minMove;

            SolRet30 = solRet30;
            BtcRet30 = btcRet30;
            SolBtcRet30 = solBtcRet30;

            SolRet1 = solRet1;
            SolRet3 = solRet3;
            BtcRet1 = btcRet1;
            BtcRet3 = btcRet3;

            FngNorm = fngNorm;
            DxyChg30 = dxyChg30;
            GoldChg30 = goldChg30;

            BtcVs200 = btcVs200;

            SolRsiCenteredScaled = solRsiCenteredScaled;
            RsiSlope3Scaled = rsiSlope3Scaled;

            GapBtcSol1 = gapBtcSol1;
            GapBtcSol3 = gapBtcSol3;

            AtrPct = atrPct;
            DynVol = dynVol;

            SolAboveEma50 = solAboveEma50;
            SolEma50vs200 = solEma50vs200;
            BtcEma50vs200 = btcEma50vs200;

            _featuresVector = BuildFeatureVector();
            ValidateFinite(_featuresVector);
        }

        private double[] BuildFeatureVector()
        {
            return new[]
            {
                SolRet30,
                BtcRet30,
                SolBtcRet30,

                SolRet1,
                SolRet3,
                BtcRet1,
                BtcRet3,

                FngNorm,
                DxyChg30,
                GoldChg30,

                BtcVs200,

                SolRsiCenteredScaled,
                RsiSlope3Scaled,

                GapBtcSol1,
                GapBtcSol3,

                RegimeDown ? 1.0 : 0.0,
                AtrPct,
                DynVol,
                HardRegime == 2 ? 1.0 : 0.0,

                SolAboveEma50,
                SolEma50vs200,
                BtcEma50vs200,
            };
        }

        private static void ValidateFinite(double[] v)
        {
            for (int i = 0; i < v.Length; i++)
            {
                var x = v[i];

                if (double.IsNaN(x) || double.IsInfinity(x))
                    throw new InvalidOperationException($"[CausalDataRow] Non-finite feature value at index {i}: {x}.");
            }
        }
    }
}
