using SolSignalModel1D_Backtest.Core.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Gaps;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Indicators;
using CausalDataRowDto = SolSignalModel1D_Backtest.Core.Causal.Data.CausalDataRow;
using Corendicators = SolSignalModel1D_Backtest.Core.Data.Indicators.Indicators;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;
using LabeledCausalRowDto = SolSignalModel1D_Backtest.Core.Causal.Data.LabeledCausalRow;

namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder
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

		// Сейчас лейблинг 1m делается только по SOL.
		// Если позже появится лейблинг по другим символам — символ нужно поднять в параметры BuildDailyRows.
		private const string Sol1mSymbol = "SOLUSDT";

		public static DailyRowsBuildResult BuildDailyRows (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			IReadOnlyList<Candle1m> solAll1m,
			Dictionary<DateTime, double> fngHistory,
			Dictionary<DateTime, double> dxySeries,
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily,
			TimeZoneInfo nyTz )
			{
			if (solWinTrain == null) throw new ArgumentNullException (nameof (solWinTrain));
			if (btcWinTrain == null) throw new ArgumentNullException (nameof (btcWinTrain));
			if (paxgWinTrain == null) throw new ArgumentNullException (nameof (paxgWinTrain));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (solAll1m == null) throw new ArgumentNullException (nameof (solAll1m));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			if (solAll1m.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] solAll1m is required for labeling.");

			if (fngHistory == null || fngHistory.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] fngHistory is null or empty.");

			if (dxySeries == null || dxySeries.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] dxySeries is null or empty.");

			if (paxgWinTrain.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] paxgWinTrain is null or empty.");

			SeriesGuards.EnsureStrictlyAscendingUtc (solAll6h, c => c.OpenTimeUtc, "RowBuilder.solAll6h");
			SeriesGuards.EnsureStrictlyAscendingUtc (solWinTrain, c => c.OpenTimeUtc, "RowBuilder.solWinTrain");
			SeriesGuards.EnsureStrictlyAscendingUtc (btcWinTrain, c => c.OpenTimeUtc, "RowBuilder.btcWinTrain");
			SeriesGuards.EnsureStrictlyAscendingUtc (paxgWinTrain, c => c.OpenTimeUtc, "RowBuilder.paxgWinTrain");
			SeriesGuards.EnsureStrictlyAscendingUtc (solAll1m, c => c.OpenTimeUtc, "RowBuilder.solAll1m");

			// Сканируем дыры в 1m один раз. Дальше только проверяем пересечения по окнам дней.
			var sol1mGaps = CandleGapScanner.Scan1mGaps (solAll1m, symbol: Sol1mSymbol, seriesName: "RowBuilder.solAll1m");
			var sol1mGapJournal = new CandleGapJournal (symbol: Sol1mSymbol, interval: "1m");

			var btcIndexByOpen = BuildIndexByOpenUtc (btcWinTrain, "RowBuilder.btcWinTrain");
			var paxgIndexByOpen = BuildIndexByOpenUtc (paxgWinTrain, "RowBuilder.paxgWinTrain");

			var solAtr = Corendicators.ComputeAtr6h (solAll6h, AtrPeriod);
			var solRsi = Corendicators.ComputeRsi6h (solWinTrain, RsiPeriod);
			var btcSma200 = Corendicators.ComputeSma6h (btcWinTrain, BtcSmaPeriod);

			var solEma50 = Corendicators.ComputeEma6h (solAll6h, SolEmaFast);
			var solEma200 = Corendicators.ComputeEma6h (solAll6h, SolEmaSlow);

			var btcEma50 = Corendicators.ComputeEma6h (btcWinTrain, BtcEmaFast);
			var btcEma200 = Corendicators.ComputeEma6h (btcWinTrain, BtcEmaSlow);

			var last6h = solAll6h[solAll6h.Count - 1];
			var maxExitUtc = last6h.OpenTimeUtc.AddHours (6);

			var minCfg = new MinMoveConfig ();
			var minState = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var minMoveHistory = new List<MinMoveHistoryRow> (solWinTrain.Count);

			var causalRows = new List<CausalDataRowDto> (solWinTrain.Count);
			var labeledRows = new List<LabeledCausalRowDto> (solWinTrain.Count);

			for (int solIdx = 0; solIdx < solWinTrain.Count; solIdx++)
				{
				var c = solWinTrain[solIdx];
				DateTime openUtc = c.OpenTimeUtc;

				using var _causalityScope = Infra.Causality.CausalityGuard.Begin ("RowBuilder.BuildDailyRows(day)", openUtc);

				var ny = TimeZoneInfo.ConvertTimeFromUtc (openUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				DateTime exitUtc = CoreWindowing.ComputeBaselineExitUtc (openUtc, nyTz);
				if (exitUtc >= maxExitUtc)
					continue;

				// Если в минутном пути есть дыра, которая пересекает baseline-окно дня — день нельзя лейблить.
				// Политика:
				// - известная дыра: лог + skip day;
				// - неизвестная: fail, чтобы зафиксировать новый gap в known-реестре.
				if (sol1mGaps.Count > 0)
					{
					bool hasKnownHit = false;

					for (int gi = 0; gi < sol1mGaps.Count; gi++)
						{
						var g = sol1mGaps[gi];
						if (!CandleGapScanner.OverlapsWindow (g, openUtc, exitUtc))
							continue;

						bool isKnown = CandleDataGaps.TryMatchKnownGap (
							symbol: Sol1mSymbol,
							interval: "1m",
							expectedStartUtc: g.ExpectedStartUtc,
							actualStartUtc: g.ActualStartUtc,
							out _);

						sol1mGapJournal.AppendSkipDay (
							symbol: Sol1mSymbol,
							interval: "1m",
							dayUtc: openUtc.ToCausalDateUtc (),
							windowStartUtc: openUtc,
							windowEndUtcExclusive: exitUtc,
							expectedStartUtc: g.ExpectedStartUtc,
							actualStartUtc: g.ActualStartUtc,
							missingBars: g.MissingBars1m,
							isKnown: isKnown);

						if (!isKnown)
							{
							throw new InvalidOperationException (
								$"[RowBuilder] unknown 1m gap intersects day window. " +
								$"day={openUtc:O}, window=[{openUtc:O}..{exitUtc:O}), " +
								$"gap=[{g.ExpectedStartUtc:O}..{g.ActualStartUtc:O}), missingBars={g.MissingBars1m}. " +
								"Добавь gap в CandleDataGaps (known) и повтори прогон.");
							}

						hasKnownHit = true;
						}

					if (hasKnownHit)
						continue;
					}

				if (!btcIndexByOpen.TryGetValue (openUtc, out int btcIdx))
					throw new InvalidOperationException ($"[RowBuilder] no BTC 6h candle matching SOL candle at {openUtc:O}.");

				if (!paxgIndexByOpen.TryGetValue (openUtc, out int gIdx))
					throw new InvalidOperationException ($"[RowBuilder] no PAXG candle for SOL entry {openUtc:O}.");

				if (WarmupGuards.ShouldSkipDailyRowWarmup (
					solIdx: solIdx,
					btcIdx: btcIdx,
					goldIdx: gIdx,
					retLookbackMax: 30,
					dynVolLookbackWindows: DynVolLookbackWindows,
					goldLookbackWindows: 30,
					out var warmupSkipReason))
					{
					continue;
					}

				double solClose = c.Close;
				if (solClose <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive SOL close at {openUtc:O}.");

				double solRet1 = Corendicators.Ret6h (solWinTrain, solIdx, 1);
				double solRet3 = Corendicators.Ret6h (solWinTrain, solIdx, 3);
				double solRet30 = Corendicators.Ret6h (solWinTrain, solIdx, 30);

				double btcRet1 = Corendicators.Ret6h (btcWinTrain, btcIdx, 1);
				double btcRet3 = Corendicators.Ret6h (btcWinTrain, btcIdx, 3);
				double btcRet30 = Corendicators.Ret6h (btcWinTrain, btcIdx, 30);

				if (double.IsNaN (solRet1) || double.IsNaN (solRet3) || double.IsNaN (solRet30) ||
					double.IsNaN (btcRet1) || double.IsNaN (btcRet3) || double.IsNaN (btcRet30))
					{
					continue;
					}

				double solBtcRet30 = solRet30 - btcRet30;
				double gapBtcSol1 = btcRet1 - solRet1;
				double gapBtcSol3 = btcRet3 - solRet3;

				if (!solEma50.TryGetValue (openUtc, out double solEma50Val)) continue;
				if (!solEma200.TryGetValue (openUtc, out double solEma200Val)) continue;
				if (!btcEma50.TryGetValue (openUtc, out double btcEma50Val)) continue;
				if (!btcEma200.TryGetValue (openUtc, out double btcEma200Val)) continue;

				if (solEma50Val <= 0 || solEma200Val <= 0 || btcEma50Val <= 0 || btcEma200Val <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive EMA values at {openUtc:O}.");

				double solAboveEma50 = (solClose - solEma50Val) / solEma50Val;
				double solEma50vs200 = (solEma50Val - solEma200Val) / solEma200Val;
				double btcEma50vs200 = (btcEma50Val - btcEma200Val) / btcEma200Val;

				var causalDayUtc = openUtc.ToCausalDateUtc ();

				double fng = Corendicators.PickNearestFng (fngHistory, causalDayUtc);
				double fngNorm = (fng - 50.0) / 50.0;

				double dxyChg30 = Corendicators.GetDxyChange30 (dxySeries, causalDayUtc);
				dxyChg30 = Math.Clamp (dxyChg30, -0.03, 0.03);

				int g30 = gIdx - 30;
				if (g30 < 0) continue;

				double gNow = paxgWinTrain[gIdx].Close;
				double gPast = paxgWinTrain[g30].Close;

				if (gNow <= 0 || gPast <= 0)
					throw new InvalidOperationException ($"[RowBuilder] invalid PAXG close prices at {openUtc:O}.");

				double goldChg30 = gNow / gPast - 1.0;

				if (!btcSma200.TryGetValue (openUtc, out double sma200))
					continue;

				if (sma200 <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive BTC 200SMA at {openUtc:O}.");

				double btcClose = btcWinTrain[btcIdx].Close;
				double btcVs200 = (btcClose - sma200) / sma200;

				if (!solRsi.TryGetValue (openUtc, out double solRsiVal))
					{
					// RSI вычисляется по ключам OpenTimeUtc входного ряда. После прогрева отсутствие ключа
					// означает дыры/рассинхрон сетки, а не "ещё рано".
					if (solIdx >= RsiPeriod)
						{
						var diag = IndicatorSeriesDiagnostics.DescribeMissingKey (
							series: solRsi,
							seriesKey: "RSI6h",
							requiredUtc: openUtc,
							neighbors: 10);

						throw new InvalidOperationException (
							$"[RowBuilder] RSI map missing at {openUtc:O} (solIdx={solIdx}, RsiPeriod={RsiPeriod}). {diag}");
						}

					continue;
					}

				// RSI-slope по барной сетке: устойчив к "вырезанным выходным", где time-based openUtc-6h*steps
				// может попадать в timestamp, отсутствующий в RSI-словаре.
				double rsiSlope3 = Corendicators.GetRsiSlopeByBars (solRsi, solWinTrain, solIdx, RsiSlopeSteps, "RSI6h");
				if (double.IsNaN (rsiSlope3))
					continue;

				double solRsiCentered = solRsiVal - 50.0;

				double dynVol = Corendicators.ComputeDynVol6h (solWinTrain, solIdx, DynVolLookbackWindows);
				if (double.IsNaN (dynVol) || double.IsInfinity (dynVol) || dynVol <= 0)
					throw new InvalidOperationException ($"[RowBuilder] dynVol is non-positive at {openUtc:O}.");

				if (!solAtr.TryGetValue (openUtc, out double atrAbs))
					continue;

				if (atrAbs <= 0)
					throw new InvalidOperationException ($"[RowBuilder] ATR is non-positive at {openUtc:O}.");

				double atrPct = atrAbs / solClose;

				bool isDownRegime = solRet30 < DownSol30Thresh || btcRet30 < DownBtc30Thresh;
				int hardRegime = Math.Abs (solRet30) > 0.10 || atrPct > 0.035 ? 2 : 1;
				bool isMorning = CoreWindowing.IsNyMorning (openUtc, nyTz);

				var mm = MinMoveEngine.ComputeAdaptive (
					asOfUtc: openUtc,
					regimeDown: isDownRegime,
					atrPct: atrPct,
					dynVol: dynVol,
					historyRows: minMoveHistory,
					cfg: minCfg,
					state: minState);

				double minMove = mm.MinMove;

				var causal = new CausalDataRowDto (
					dateUtc: openUtc,
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

				// Создание окна после проверки known gaps выше.
				// Create(...) дополнительно валидирует, что entryUtc действительно присутствует как минута.
				var window = Baseline1mWindow.Create (solAll1m, openUtc, exitUtc);

				int firstPassDir;
				DateTime? firstPassTimeUtc;
				double pathUp, pathDown;

				int trueLabel = PathLabeler.AssignLabel (
					window: window,
					entryPrice: solClose,
					minMove: minMove,
					firstPassDir: out firstPassDir,
					firstPassTimeUtc: out firstPassTimeUtc,
					reachedUpPct: out pathUp,
					reachedDownPct: out pathDown);

				bool factMicroUp = false;
				bool factMicroDown = false;

				if (trueLabel == 1)
					{
					if (pathUp > Math.Abs (pathDown) + 0.001) factMicroUp = true;
					else if (Math.Abs (pathDown) > pathUp + 0.001) factMicroDown = true;
					}

				// Добавляем строки только если день успешно лейблится.
				causalRows.Add (causal);

				labeledRows.Add (new LabeledCausalRowDto (
					causal: causal,
					trueLabel: trueLabel,
					factMicroUp: factMicroUp,
					factMicroDown: factMicroDown));

				double realizedAmp = Math.Max (pathUp, Math.Abs (pathDown));
				if (double.IsNaN (realizedAmp) || double.IsInfinity (realizedAmp) || realizedAmp < 0)
					throw new InvalidOperationException ($"[RowBuilder] invalid realizedAmp={realizedAmp} at {openUtc:O}.");

				minMoveHistory.Add (new MinMoveHistoryRow (causalDayUtc, realizedAmp));
				}

			return new DailyRowsBuildResult
				{
				CausalRows = causalRows,
				LabeledRows = labeledRows
				};
			}

		private static Dictionary<DateTime, int> BuildIndexByOpenUtc ( List<Candle6h> xs, string seriesName )
			{
			var dict = new Dictionary<DateTime, int> (xs.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var t = xs[i].OpenTimeUtc;

				if (t.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[RowBuilder] {seriesName}: OpenTimeUtc must be UTC, got Kind={t.Kind}, t={t:O}.");

				if (!dict.TryAdd (t, i))
					throw new InvalidOperationException ($"[RowBuilder] {seriesName}: duplicate OpenTimeUtc detected: {t:O}.");
				}

			return dict;
			}
		}
	}
