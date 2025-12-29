using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Causal.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Indicators;
using CausalDataRowDto = SolSignalModel1D_Backtest.Core.Causal.Data.CausalDataRow;
using Corendicators = SolSignalModel1D_Backtest.Core.Causal.Data.Indicators.Indicators;
using LabeledCausalRowDto = SolSignalModel1D_Backtest.Core.Causal.Data.LabeledCausalRow;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder
{
    public static class RowBuilder
    {
        private const int AtrPeriod = 14;
        private const int RsiPeriod = 14;
        private const int BtcSmaPeriod = 200;

        private const double DownSol30Thresh = -0.07;
        private const double DownBtc30Thresh = -0.05;

        private const int SolEmaFast = 50;
        private const int SolEmaSlow = 200;
        private const int BtcEmaFast = 50;
        private const int BtcEmaSlow = 200;

        private const int DynVolLookbackWindows = 10;
        private const int RsiSlopeSteps = 3;

        private const string Sol1mSymbol = "SOLUSDT";

        private static readonly string[] Candle6hFeatureNames = new[]
        {
            nameof(CausalDataRowDto.SolRet30),
            nameof(CausalDataRowDto.BtcRet30),
            nameof(CausalDataRowDto.SolBtcRet30),

            nameof(CausalDataRowDto.SolRet1),
            nameof(CausalDataRowDto.SolRet3),
            nameof(CausalDataRowDto.BtcRet1),
            nameof(CausalDataRowDto.BtcRet3),

            nameof(CausalDataRowDto.GoldChg30),
            nameof(CausalDataRowDto.BtcVs200),

            nameof(CausalDataRowDto.SolRsiCenteredScaled),
            nameof(CausalDataRowDto.RsiSlope3Scaled),

            nameof(CausalDataRowDto.GapBtcSol1),
            nameof(CausalDataRowDto.GapBtcSol3),

            "RegimeDownFlag",
            nameof(CausalDataRowDto.AtrPct),
            nameof(CausalDataRowDto.DynVol),
            "HardRegimeIs2Flag",

            nameof(CausalDataRowDto.SolAboveEma50),
            nameof(CausalDataRowDto.SolEma50vs200),
            nameof(CausalDataRowDto.BtcEma50vs200),
        };

        private static readonly string[] IndicatorFeatureNames = new[]
        {
            nameof(CausalDataRowDto.FngNorm),
            nameof(CausalDataRowDto.DxyChg30),
        };

        public static DailyRowsBuildResult BuildDailyRows(
            List<Candle6h> solWinTrain,
            List<Candle6h> btcWinTrain,
            List<Candle6h> paxgWinTrain,
            List<Candle6h> solAll6h,
            IReadOnlyList<Candle1m> solAll1m,
            Dictionary<DateTime, double> fngHistory,
            Dictionary<DateTime, double> dxySeries,
            Dictionary<DateTime, (double Funding, double OI)>? extraDaily,
            TimeZoneInfo nyTz)
        {
            if (solWinTrain == null) throw new ArgumentNullException(nameof(solWinTrain));
            if (btcWinTrain == null) throw new ArgumentNullException(nameof(btcWinTrain));
            if (paxgWinTrain == null) throw new ArgumentNullException(nameof(paxgWinTrain));
            if (solAll6h == null) throw new ArgumentNullException(nameof(solAll6h));
            if (solAll1m == null) throw new ArgumentNullException(nameof(solAll1m));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            if (solAll1m.Count == 0)
                throw new InvalidOperationException("[RowBuilder] solAll1m is required for labeling.");

            if (fngHistory == null || fngHistory.Count == 0)
                throw new InvalidOperationException("[RowBuilder] fngHistory is null or empty.");

            if (dxySeries == null || dxySeries.Count == 0)
                throw new InvalidOperationException("[RowBuilder] dxySeries is null or empty.");

            if (paxgWinTrain.Count == 0)
                throw new InvalidOperationException("[RowBuilder] paxgWinTrain is null or empty.");

            SeriesGuards.EnsureStrictlyAscendingUtc(solAll6h, c => c.OpenTimeUtc, "RowBuilder.solAll6h");
            SeriesGuards.EnsureStrictlyAscendingUtc(solWinTrain, c => c.OpenTimeUtc, "RowBuilder.solWinTrain");
            SeriesGuards.EnsureStrictlyAscendingUtc(btcWinTrain, c => c.OpenTimeUtc, "RowBuilder.btcWinTrain");
            SeriesGuards.EnsureStrictlyAscendingUtc(paxgWinTrain, c => c.OpenTimeUtc, "RowBuilder.paxgWinTrain");
            SeriesGuards.EnsureStrictlyAscendingUtc(solAll1m, c => c.OpenTimeUtc, "RowBuilder.solAll1m");

            SeriesGuards.EnsureUniformStepUtc(solAll6h, c => c.OpenTimeUtc, TimeSpan.FromHours(6), "RowBuilder.solAll6h");

            var sol1mGaps = CandleGapScanner.Scan1mGaps(solAll1m, symbol: Sol1mSymbol, seriesName: "RowBuilder.solAll1m");
            var sol1mGapJournal = new CandleGapJournal(symbol: Sol1mSymbol, interval: "1m");

            var btcIndexByOpen = BuildIndexByOpenUtc(btcWinTrain, "RowBuilder.btcWinTrain");
            var paxgIndexByOpen = BuildIndexByOpenUtc(paxgWinTrain, "RowBuilder.paxgWinTrain");

            var solAtr = Corendicators.ComputeAtr6h(solAll6h, AtrPeriod);
            var solRsi = Corendicators.ComputeRsi6h(solWinTrain, RsiPeriod);
            var btcSma200 = Corendicators.ComputeSma6h(btcWinTrain, BtcSmaPeriod);

            var solEma50 = Corendicators.ComputeEma6h(solAll6h, SolEmaFast);
            var solEma200 = Corendicators.ComputeEma6h(solAll6h, SolEmaSlow);

            var btcEma50 = Corendicators.ComputeEma6h(btcWinTrain, BtcEmaFast);
            var btcEma200 = Corendicators.ComputeEma6h(btcWinTrain, BtcEmaSlow);

            var last6h = solAll6h[solAll6h.Count - 1];
            var maxExitUtc = last6h.OpenTimeUtc.AddHours(6);

            var minCfg = new MinMoveConfig();
            var minState = new MinMoveState
            {
                EwmaVol = 0.0,
                QuantileQ = 0.0,
                LastQuantileTune = DateTime.MinValue
            };

            var minMoveHistory = new List<MinMoveHistoryRow>(solWinTrain.Count);

            var causalRows = new List<CausalDataRowDto>(solWinTrain.Count);
            var labeledRows = new List<LabeledCausalRowDto>(solWinTrain.Count);
            int ambiguousHitCount = 0;
            DateTime? firstAmbiguousEntryUtc = null;

            var leakProbe = new RowFeatureLeakageProbe(maxRows: 20);

            for (int solIdx = 0; solIdx < solWinTrain.Count; solIdx++)
            {
                var c = solWinTrain[solIdx];
                DateTime entryUtc = c.OpenTimeUtc;

                using var _causalityScope = Infra.Causality.CausalityGuard.Begin("RowBuilder.BuildDailyRows(day)", entryUtc);

                // Weekend отсекаем типом NyTradingEntryUtc (единая точка контракта).
                var openEntryUtc = new EntryUtc(entryUtc);
                if (!NyWindowing.TryCreateNyTradingEntryUtc(openEntryUtc, nyTz, out var nyEntryUtc))
                    continue;

                DateTime exitUtc = NyWindowing.ComputeBaselineExitUtc(nyEntryUtc, nyTz).Value;
                if (exitUtc >= maxExitUtc)
                    continue;

                int solFeatureIdx = solIdx - 1;
                if (solFeatureIdx < 0)
                    continue;

                var solFeatureCandle = solWinTrain[solFeatureIdx];
                DateTime featureOpenUtc = solFeatureCandle.OpenTimeUtc;
                DateTime featureCloseUtc = featureOpenUtc.AddHours(6);

                if (featureOpenUtc != entryUtc.AddHours(-6))
                {
                    throw new InvalidOperationException(
                        $"[RowBuilder] 6h alignment mismatch: featureOpenUtc={featureOpenUtc:O}, " +
                        $"expected={entryUtc.AddHours(-6):O}, entryUtc={entryUtc:O}.");
                }

                EnsureClosedBeforeEntry(featureOpenUtc, featureCloseUtc, entryUtc, "RowBuilder:6h-feature");

                if (sol1mGaps.Count > 0)
                {
                    bool hasKnownHit = false;

                    for (int gi = 0; gi < sol1mGaps.Count; gi++)
                    {
                        var g = sol1mGaps[gi];
                        if (!CandleGapScanner.OverlapsWindow(g, entryUtc, exitUtc))
                            continue;

                        bool isKnown = CandleDataGaps.TryMatchKnownGap(
                            symbol: Sol1mSymbol,
                            interval: "1m",
                            expectedStartUtc: g.ExpectedStartUtc,
                            actualStartUtc: g.ActualStartUtc,
                            out _);

                        sol1mGapJournal.AppendSkipDay(
                            symbol: Sol1mSymbol,
                            interval: "1m",
                            dayUtc: entryUtc.ToCausalDateUtc(),
                            windowStartUtc: entryUtc,
                            windowEndUtcExclusive: exitUtc,
                            expectedStartUtc: g.ExpectedStartUtc,
                            actualStartUtc: g.ActualStartUtc,
                            missingBars: g.MissingBars1m,
                            isKnown: isKnown);

                        if (!isKnown)
                        {
                            throw new InvalidOperationException(
                                $"[RowBuilder] unknown 1m gap intersects day window. " +
                                $"day={entryUtc:O}, window=[{entryUtc:O}..{exitUtc:O}), " +
                                $"gap=[{g.ExpectedStartUtc:O}..{g.ActualStartUtc:O}), missingBars={g.MissingBars1m}. " +
                                "Добавь gap в CandleDataGaps (known) и повтори прогон.");
                        }

                        hasKnownHit = true;
                    }

                    if (hasKnownHit)
                        continue;
                }

                if (!btcIndexByOpen.TryGetValue(featureOpenUtc, out int btcIdx))
                    throw new InvalidOperationException($"[RowBuilder] no BTC 6h candle matching SOL feature candle at {featureOpenUtc:O}.");

                if (!paxgIndexByOpen.TryGetValue(featureOpenUtc, out int gIdx))
                    throw new InvalidOperationException($"[RowBuilder] no PAXG candle for SOL feature candle {featureOpenUtc:O}.");

                EnsureClosedBeforeEntry(featureOpenUtc, featureOpenUtc.AddHours(6), entryUtc, "RowBuilder:btc-6h");
                EnsureClosedBeforeEntry(featureOpenUtc, featureOpenUtc.AddHours(6), entryUtc, "RowBuilder:paxg-6h");

                if (WarmupGuards.ShouldSkipDailyRowWarmup(
                    solIdx: solFeatureIdx,
                    btcIdx: btcIdx,
                    goldIdx: gIdx,
                    retLookbackMax: 30,
                    dynVolLookbackWindows: DynVolLookbackWindows,
                    goldLookbackWindows: 30,
                    out var warmupSkipReason))
                {
                    continue;
                }

                double entryPrice = c.Open;
                if (entryPrice <= 0)
                    throw new InvalidOperationException($"[RowBuilder] non-positive SOL entry price at {entryUtc:O}.");

                double solRet1 = Corendicators.Ret6h(solWinTrain, solFeatureIdx, 1);
                double solRet3 = Corendicators.Ret6h(solWinTrain, solFeatureIdx, 3);
                double solRet30 = Corendicators.Ret6h(solWinTrain, solFeatureIdx, 30);

                double btcRet1 = Corendicators.Ret6h(btcWinTrain, btcIdx, 1);
                double btcRet3 = Corendicators.Ret6h(btcWinTrain, btcIdx, 3);
                double btcRet30 = Corendicators.Ret6h(btcWinTrain, btcIdx, 30);

                if (double.IsNaN(solRet1) || double.IsNaN(solRet3) || double.IsNaN(solRet30) ||
                    double.IsNaN(btcRet1) || double.IsNaN(btcRet3) || double.IsNaN(btcRet30))
                {
                    continue;
                }

                double solBtcRet30 = solRet30 - btcRet30;
                double gapBtcSol1 = btcRet1 - solRet1;
                double gapBtcSol3 = btcRet3 - solRet3;

                if (!solEma50.TryGetValue(featureOpenUtc, out double solEma50Val)) continue;
                if (!solEma200.TryGetValue(featureOpenUtc, out double solEma200Val)) continue;
                if (!btcEma50.TryGetValue(featureOpenUtc, out double btcEma50Val)) continue;
                if (!btcEma200.TryGetValue(featureOpenUtc, out double btcEma200Val)) continue;

                if (solEma50Val <= 0 || solEma200Val <= 0 || btcEma50Val <= 0 || btcEma200Val <= 0)
                    throw new InvalidOperationException($"[RowBuilder] non-positive EMA values at {featureOpenUtc:O}.");

                double solAboveEma50 = (entryPrice - solEma50Val) / solEma50Val;
                double solEma50vs200 = (solEma50Val - solEma200Val) / solEma200Val;
                double btcEma50vs200 = (btcEma50Val - btcEma200Val) / btcEma200Val;

                var causalDayUtc = entryUtc.ToCausalDateUtc();
                var indicatorDayUtc = causalDayUtc.AddDays(-1);

                double fng = Corendicators.PickNearestFng(fngHistory, indicatorDayUtc);
                double fngNorm = (fng - 50.0) / 50.0;

                double dxyChg30 = Corendicators.GetDxyChange30(dxySeries, indicatorDayUtc);
                dxyChg30 = Math.Clamp(dxyChg30, -0.03, 0.03);

                int g30 = gIdx - 30;
                if (g30 < 0) continue;

                double gNow = paxgWinTrain[gIdx].Close;
                double gPast = paxgWinTrain[g30].Close;

                if (gNow <= 0 || gPast <= 0)
                    throw new InvalidOperationException($"[RowBuilder] invalid PAXG close prices at {featureOpenUtc:O}.");

                double goldChg30 = gNow / gPast - 1.0;

                if (!btcSma200.TryGetValue(featureOpenUtc, out double sma200))
                    continue;

                if (sma200 <= 0)
                    throw new InvalidOperationException($"[RowBuilder] non-positive BTC 200SMA at {featureOpenUtc:O}.");

                double btcClose = btcWinTrain[btcIdx].Close;
                double btcVs200 = (btcClose - sma200) / sma200;

                if (!solRsi.TryGetValue(featureOpenUtc, out double solRsiVal))
                {
                    if (solFeatureIdx >= RsiPeriod)
                    {
                        var diag = IndicatorSeriesDiagnostics.DescribeMissingKey(
                            series: solRsi,
                            seriesKey: "RSI6h",
                            requiredUtc: featureOpenUtc,
                            neighbors: 10);

                        throw new InvalidOperationException(
                            $"[RowBuilder] RSI map missing at {featureOpenUtc:O} (solIdx={solFeatureIdx}, RsiPeriod={RsiPeriod}). {diag}");
                    }

                    continue;
                }

                double rsiSlope3 = Corendicators.GetRsiSlopeByBars(solRsi, solWinTrain, solFeatureIdx, RsiSlopeSteps, "RSI6h");
                if (double.IsNaN(rsiSlope3))
                    continue;

                double solRsiCentered = solRsiVal - 50.0;

                double dynVol = Corendicators.ComputeDynVol6h(solWinTrain, solFeatureIdx, DynVolLookbackWindows);
                if (double.IsNaN(dynVol) || double.IsInfinity(dynVol) || dynVol <= 0)
                    throw new InvalidOperationException($"[RowBuilder] dynVol is non-positive at {featureOpenUtc:O}.");

                if (!solAtr.TryGetValue(featureOpenUtc, out double atrAbs))
                    continue;

                if (atrAbs <= 0)
                    throw new InvalidOperationException($"[RowBuilder] ATR is non-positive at {featureOpenUtc:O}.");

                double atrPct = atrAbs / entryPrice;

                bool isDownRegime = solRet30 < DownSol30Thresh || btcRet30 < DownBtc30Thresh;
                int hardRegime = Math.Abs(solRet30) > 0.10 || atrPct > 0.035 ? 2 : 1;

                bool isMorning = NyWindowing.IsNyMorning(nyEntryUtc, nyTz);

                var mm = MinMoveEngine.ComputeAdaptive(
                    asOfUtc: entryUtc,
                    regimeDown: isDownRegime,
                    atrPct: atrPct,
                    dynVol: dynVol,
                    historyRows: minMoveHistory,
                    cfg: minCfg,
                    state: minState);

                double minMove = mm.MinMove;

                var leakRow = leakProbe.BeginRow(entryUtc);
                for (int i = 0; i < Candle6hFeatureNames.Length; i++)
                    leakRow.MarkCandle6h(Candle6hFeatureNames[i], featureOpenUtc, featureCloseUtc);

                for (int i = 0; i < IndicatorFeatureNames.Length; i++)
                    leakRow.MarkIndicator(IndicatorFeatureNames[i], indicatorDayUtc);

                var causal = new CausalDataRowDto(
                    entryUtc: nyEntryUtc,
                    regimeDown: isDownRegime,
                    isMorning: isMorning,
                    hardRegime: hardRegime,
                    minMove: minMove,

                    solRet30: solRet30,
                    btcRet30: btcRet30,
                    solBtcRet30: solBtcRet30,

                    solRet1: solRet1,
                    solRet3: solRet3,
                    btcRet1: btcRet1,
                    btcRet3: btcRet3,

                    fngNorm: fngNorm,
                    dxyChg30: dxyChg30,
                    goldChg30: goldChg30,

                    btcVs200: btcVs200,

                    solRsiCenteredScaled: solRsiCentered / 100.0,
                    rsiSlope3Scaled: rsiSlope3 / 100.0,

                    gapBtcSol1: gapBtcSol1,
                    gapBtcSol3: gapBtcSol3,

                    atrPct: atrPct,
                    dynVol: dynVol,

                    solAboveEma50: solAboveEma50,
                    solEma50vs200: solEma50vs200,
                    btcEma50vs200: btcEma50vs200);

                var window = Baseline1mWindow.Create(solAll1m, entryUtc, exitUtc);

                int firstPassDir;
                DateTime? firstPassTimeUtc;
                double pathUp, pathDown;

                int trueLabel = PathLabeler.AssignLabel(
                    window: window,
                    entryPrice: entryPrice,
                    minMove: minMove,
                    firstPassDir: out firstPassDir,
                    firstPassTimeUtc: out firstPassTimeUtc,
                    reachedUpPct: out pathUp,
                    reachedDownPct: out pathDown,
                    ambiguousHitSameMinute: out var ambiguousHitSameMinute);

                double realizedAmp = Math.Max(pathUp, Math.Abs(pathDown));
                if (double.IsNaN(realizedAmp) || double.IsInfinity(realizedAmp) || realizedAmp < 0)
                    throw new InvalidOperationException($"[RowBuilder] invalid realizedAmp={realizedAmp} at {entryUtc:O}.");

                if (ambiguousHitSameMinute)
                {
                    ambiguousHitCount++;
                    if (!firstAmbiguousEntryUtc.HasValue)
                        firstAmbiguousEntryUtc = entryUtc;

                    EnsureMinMoveHistoryMonotonic(minMoveHistory, causalDayUtc, entryUtc);
                    minMoveHistory.Add(new MinMoveHistoryRow(causalDayUtc, realizedAmp));
                    continue;
                }

                var microTruth = OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.NonFlatTruth);

                if (trueLabel == 1)
                {
                    if (pathUp > Math.Abs(pathDown) + 0.001)
                        microTruth = OptionalValue<MicroTruthDirection>.Present(MicroTruthDirection.Up);
                    else if (Math.Abs(pathDown) > pathUp + 0.001)
                        microTruth = OptionalValue<MicroTruthDirection>.Present(MicroTruthDirection.Down);
                    else
                        microTruth = OptionalValue<MicroTruthDirection>.Missing(MissingReasonCodes.MicroNeutral);
                }

                causalRows.Add(causal);

                labeledRows.Add(new LabeledCausalRowDto(
                    causal: causal,
                    trueLabel: trueLabel,
                    microTruth: microTruth));

                leakProbe.Commit(leakRow);

                EnsureMinMoveHistoryMonotonic(minMoveHistory, causalDayUtc, entryUtc);
                minMoveHistory.Add(new MinMoveHistoryRow(causalDayUtc, realizedAmp));
            }

            if (ambiguousHitCount > 0)
            {
                string first = firstAmbiguousEntryUtc.HasValue ? firstAmbiguousEntryUtc.Value.ToString("O") : "n/a";
                Console.WriteLine(
                    $"[RowBuilder] исключены дни с двойным пробоем в одной минуте: count={ambiguousHitCount}, firstEntryUtc={first}.");
            }

            leakProbe.FlushAndThrowIfLeak();

            return new DailyRowsBuildResult
            {
                CausalRows = causalRows,
                LabeledRows = labeledRows
            };
        }

        private static Dictionary<DateTime, int> BuildIndexByOpenUtc(List<Candle6h> xs, string seriesName)
        {
            var dict = new Dictionary<DateTime, int>(xs.Count);

            for (int i = 0; i < xs.Count; i++)
            {
                var t = xs[i].OpenTimeUtc;

                if (t.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[RowBuilder] {seriesName}: OpenTimeUtc must be UTC, got Kind={t.Kind}, t={t:O}.");

                if (!dict.TryAdd(t, i))
                    throw new InvalidOperationException($"[RowBuilder] {seriesName}: duplicate OpenTimeUtc detected: {t:O}.");
            }

            return dict;
        }

        private static void EnsureClosedBeforeEntry(DateTime candleOpenUtc, DateTime candleCloseUtc, DateTime entryUtc, string featureName)
        {
            if (candleCloseUtc > entryUtc)
            {
                Console.WriteLine(
                    $"[leak-probe:6h] Обнаружена незакрытая свеча: feature='{featureName}', " +
                    $"entryUtc={entryUtc:O}, candleOpenUtc={candleOpenUtc:O}, candleCloseUtc={candleCloseUtc:O}.");

                throw new InvalidOperationException(
                    $"[RowBuilder] leakage guard: feature '{featureName}' uses unclosed candle. " +
                    $"entryUtc={entryUtc:O}, candleOpenUtc={candleOpenUtc:O}, candleCloseUtc={candleCloseUtc:O}.");
            }
        }

        private static void EnsureMinMoveHistoryMonotonic(
            IReadOnlyList<MinMoveHistoryRow> history,
            DateTime currentDayUtc,
            DateTime entryUtc)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (currentDayUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[RowBuilder] currentDayUtc must be UTC. currentDayUtc={currentDayUtc:O}.");
            if (entryUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[RowBuilder] entryUtc must be UTC. entryUtc={entryUtc:O}.");

            if (history.Count == 0)
                return;

            var lastDayUtc = history[history.Count - 1].DateUtc;
            if (lastDayUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[RowBuilder] MinMoveHistoryRow.DateUtc must be UTC. lastDayUtc={lastDayUtc:O}, entryUtc={entryUtc:O}.");

            if (lastDayUtc >= currentDayUtc)
            {
                throw new InvalidOperationException(
                    $"[RowBuilder] MinMoveHistory must be strictly increasing by day. " +
                    $"lastDayUtc={lastDayUtc:O}, currentDayUtc={currentDayUtc:O}, entryUtc={entryUtc:O}.");
            }
        }

        private sealed class RowFeatureLeakageProbe
        {
            private readonly List<RowLeakRecord> _leaks = new();
            private readonly int _maxRows;

            public RowFeatureLeakageProbe(int maxRows)
            {
                if (maxRows <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "maxRows must be > 0.");

                _maxRows = maxRows;
            }

            public RowLeakProbe BeginRow(DateTime entryUtc) => new RowLeakProbe(entryUtc);

            public void Commit(RowLeakProbe row)
            {
                if (row == null) throw new ArgumentNullException(nameof(row));

                if (!row.TryGetMaxUsed(out var maxUsedUtc, out var maxFeatures))
                    return;

                var delta = maxUsedUtc - row.EntryUtc;
                if (delta <= TimeSpan.Zero)
                    return;

                _leaks.Add(new RowLeakRecord
                {
                    EntryUtc = row.EntryUtc,
                    MaxUsedUtc = maxUsedUtc,
                    MaxFutureLeak = delta,
                    LatestCandle6hUtcUsed = row.LatestCandle6hUtcUsed,
                    LatestIndicatorDayUtcUsed = row.LatestIndicatorDayUtcUsed,
                    LatestCandle1mUtcUsed = row.LatestCandle1mUtcUsed,
                    MaxFeatures = maxFeatures.ToArray()
                });
            }

            public void FlushAndThrowIfLeak()
            {
                if (_leaks.Count == 0)
                    return;

                var ordered = _leaks
                    .OrderByDescending(r => r.MaxFutureLeak)
                    .ThenBy(r => r.EntryUtc)
                    .ToList();

                var top = ordered.Take(_maxRows).ToList();

                Console.WriteLine(
                    $"[leak-probe:features] ПОДОЗРЕНИЕ: подглядывание в будущее в daily-фичах. строк={_leaks.Count}, топ={top.Count}.");

                for (int i = 0; i < top.Count; i++)
                {
                    var r = top[i];
                    var entryDayKey = r.EntryUtc.ToCausalDateUtc();

                    Console.WriteLine(
                        $"[leak-probe:features] #{i + 1} входUtc={r.EntryUtc:O}, деньКлючUtc={entryDayKey:yyyy-MM-dd}, " +
                        $"максИспользUtc={r.MaxUsedUtc:O}, максУтечкаСек={r.MaxFutureLeak.TotalSeconds:0}");

                    var maxFeatures = r.MaxFeatures.Length == 0
                        ? "(нет фич)"
                        : string.Join(", ", r.MaxFeatures.Select(FormatFeature));

                    Console.WriteLine($"[leak-probe:features]    максФичи={maxFeatures}");

                    string latest6h = r.LatestCandle6hUtcUsed.HasValue
                        ? r.LatestCandle6hUtcUsed.Value.ToString("O")
                        : "<не используется>";

                    string latestIndicators = r.LatestIndicatorDayUtcUsed.HasValue
                        ? r.LatestIndicatorDayUtcUsed.Value.ToString("yyyy-MM-dd")
                        : "<не используется>";

                    string latest1m = r.LatestCandle1mUtcUsed.HasValue
                        ? r.LatestCandle1mUtcUsed.Value.ToString("O")
                        : "<не используется>";

                    Console.WriteLine(
                        $"[leak-probe:features]    последние: свеча6h={latest6h}, индикаторы={latestIndicators}, свеча1m={latest1m}");
                }

                var topLeak = top[0];
                throw new InvalidOperationException(
                    $"[RowBuilder] future feature usage detected. " +
                    $"count={_leaks.Count}, topEntryUtc={topLeak.EntryUtc:O}, maxUsedUtc={topLeak.MaxUsedUtc:O}, " +
                    $"maxLeakSec={topLeak.MaxFutureLeak.TotalSeconds:0}.");
            }

            private static string FormatFeature(FeatureTimeInfo info)
            {
                if (info.SourceKind == FeatureSourceKind.Candle6h)
                {
                    string open = info.CandleOpenUtc.HasValue ? info.CandleOpenUtc.Value.ToString("O") : "n/a";
                    string close = info.CandleCloseUtc.HasValue ? info.CandleCloseUtc.Value.ToString("O") : "n/a";
                    return $"{info.FeatureName}@{info.UsedUtc:O} (6h: {open}..{close})";
                }

                if (info.SourceKind == FeatureSourceKind.Indicator)
                    return $"{info.FeatureName}@{info.UsedUtc:yyyy-MM-dd} (индикатор)";

                if (info.SourceKind == FeatureSourceKind.Candle1m)
                    return $"{info.FeatureName}@{info.UsedUtc:O} (1m)";

                return $"{info.FeatureName}@{info.UsedUtc:O}";
            }
        }

        private sealed class RowLeakProbe
        {
            private DateTime? _maxUsedUtc;
            private readonly List<FeatureTimeInfo> _maxFeatures = new();

            public DateTime EntryUtc { get; }

            public DateTime? LatestCandle6hUtcUsed { get; private set; }
            public DateTime? LatestIndicatorDayUtcUsed { get; private set; }
            public DateTime? LatestCandle1mUtcUsed { get; private set; }

            public RowLeakProbe(DateTime entryUtc)
            {
                if (entryUtc == default)
                    throw new ArgumentException("entryUtc must be initialized.", nameof(entryUtc));
                if (entryUtc.Kind != DateTimeKind.Utc)
                    throw new ArgumentException("entryUtc must be UTC.", nameof(entryUtc));

                EntryUtc = entryUtc;
            }

            public void MarkCandle6h(string featureName, DateTime candleOpenUtc, DateTime candleCloseUtc)
            {
                if (candleOpenUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[RowBuilder] candleOpenUtc must be UTC for feature '{featureName}'.");
                if (candleCloseUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[RowBuilder] candleCloseUtc must be UTC for feature '{featureName}'.");

                MarkFeature(featureName, candleCloseUtc, FeatureSourceKind.Candle6h, candleOpenUtc, candleCloseUtc);

                if (!LatestCandle6hUtcUsed.HasValue || candleCloseUtc > LatestCandle6hUtcUsed.Value)
                    LatestCandle6hUtcUsed = candleCloseUtc;
            }

            public void MarkIndicator(string featureName, DateTime indicatorDayUtc)
            {
                if (indicatorDayUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[RowBuilder] indicatorDayUtc must be UTC for feature '{featureName}'.");

                MarkFeature(featureName, indicatorDayUtc, FeatureSourceKind.Indicator, null, null);

                if (!LatestIndicatorDayUtcUsed.HasValue || indicatorDayUtc > LatestIndicatorDayUtcUsed.Value)
                    LatestIndicatorDayUtcUsed = indicatorDayUtc;
            }

            public void MarkCandle1m(string featureName, DateTime usedUtc)
            {
                if (usedUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[RowBuilder] usedUtc must be UTC for feature '{featureName}'.");

                MarkFeature(featureName, usedUtc, FeatureSourceKind.Candle1m, null, null);

                if (!LatestCandle1mUtcUsed.HasValue || usedUtc > LatestCandle1mUtcUsed.Value)
                    LatestCandle1mUtcUsed = usedUtc;
            }

            public bool TryGetMaxUsed(out DateTime maxUsedUtc, out IReadOnlyList<FeatureTimeInfo> maxFeatures)
            {
                if (!_maxUsedUtc.HasValue)
                {
                    maxUsedUtc = default;
                    maxFeatures = Array.Empty<FeatureTimeInfo>();
                    return false;
                }

                maxUsedUtc = _maxUsedUtc.Value;
                maxFeatures = _maxFeatures;
                return true;
            }

            private void MarkFeature(
                string featureName,
                DateTime usedUtc,
                FeatureSourceKind sourceKind,
                DateTime? candleOpenUtc,
                DateTime? candleCloseUtc)
            {
                if (string.IsNullOrWhiteSpace(featureName))
                    throw new ArgumentException("featureName must be non-empty.", nameof(featureName));

                if (usedUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[RowBuilder] usedUtc must be UTC for feature '{featureName}'.");

                if (!_maxUsedUtc.HasValue || usedUtc > _maxUsedUtc.Value)
                {
                    _maxUsedUtc = usedUtc;
                    _maxFeatures.Clear();
                }

                if (_maxUsedUtc.HasValue && usedUtc == _maxUsedUtc.Value)
                {
                    _maxFeatures.Add(new FeatureTimeInfo
                    {
                        FeatureName = featureName,
                        UsedUtc = usedUtc,
                        SourceKind = sourceKind,
                        CandleOpenUtc = candleOpenUtc,
                        CandleCloseUtc = candleCloseUtc
                    });
                }
            }
        }

        private sealed class RowLeakRecord
        {
            public DateTime EntryUtc { get; init; }
            public DateTime MaxUsedUtc { get; init; }
            public TimeSpan MaxFutureLeak { get; init; }
            public DateTime? LatestCandle6hUtcUsed { get; init; }
            public DateTime? LatestIndicatorDayUtcUsed { get; init; }
            public DateTime? LatestCandle1mUtcUsed { get; init; }
            public FeatureTimeInfo[] MaxFeatures { get; init; } = Array.Empty<FeatureTimeInfo>();
        }

        private enum FeatureSourceKind
        {
            Candle6h = 0,
            Indicator = 1,
            Candle1m = 2,
            Other = 3
        }

        private sealed class FeatureTimeInfo
        {
            public string FeatureName { get; init; } = string.Empty;
            public DateTime UsedUtc { get; init; }
            public FeatureSourceKind SourceKind { get; init; }
            public DateTime? CandleOpenUtc { get; init; }
            public DateTime? CandleCloseUtc { get; init; }
        }
    }
}

