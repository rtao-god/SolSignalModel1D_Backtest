using SolSignalModel1D_Backtest.Core.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder.Diagnostics;
using SolSignalModel1D_Backtest.Core.Utils;
using Corendicators = SolSignalModel1D_Backtest.Core.Data.Indicators.Indicators;

namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder
	{
	/// <summary>
	/// Построитель дневных строк DataRow из 6h/1m свечей и дневных индикаторов.
	/// </summary>
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

		/// <summary>
		/// Основной вариант: 6h + 1m + адаптивный minMove через MinMoveEngine.
		/// 1m ОБЯЗАТЕЛЬНЫ.
		/// </summary>
		public static List<DataRow> BuildRowsDaily (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			IReadOnlyList<Candle1m>? solAll1m,
			Dictionary<DateTime, double> fngHistory,
			Dictionary<DateTime, double> dxySeries,
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily,
			TimeZoneInfo nyTz )
			{
			if (solWinTrain == null) throw new ArgumentNullException (nameof (solWinTrain));
			if (btcWinTrain == null) throw new ArgumentNullException (nameof (btcWinTrain));
			if (paxgWinTrain == null) throw new ArgumentNullException (nameof (paxgWinTrain));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			if (solAll1m == null || solAll1m.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] solAll1m is required for path-based labels and MinMoveEngine.");

			if (fngHistory == null || fngHistory.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] fngHistory is null or empty.");

			if (dxySeries == null || dxySeries.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] dxySeries is null or empty.");

			if (paxgWinTrain.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] paxgWinTrain is null or empty.");

			// Контракт: все ряды уже отсортированы на бутстрапе; здесь только проверяем.
			SeriesGuards.EnsureStrictlyAscendingUtc (solAll6h, c => c.OpenTimeUtc, "RowBuilder.solAll6h");
			SeriesGuards.EnsureStrictlyAscendingUtc (solWinTrain, c => c.OpenTimeUtc, "RowBuilder.solWinTrain");
			SeriesGuards.EnsureStrictlyAscendingUtc (btcWinTrain, c => c.OpenTimeUtc, "RowBuilder.btcWinTrain");
			SeriesGuards.EnsureStrictlyAscendingUtc (paxgWinTrain, c => c.OpenTimeUtc, "RowBuilder.paxgWinTrain");
			SeriesGuards.EnsureStrictlyAscendingUtc (solAll1m, c => c.OpenTimeUtc, "RowBuilder.solAll1m");

			var btcIndexByOpen = BuildIndexByOpenUtc (btcWinTrain, "RowBuilder.btcWinTrain");
			var paxgIndexByOpen = BuildIndexByOpenUtc (paxgWinTrain, "RowBuilder.paxgWinTrain");

			// Индикаторы по 6h
			var solAtr = Corendicators.ComputeAtr6h (solAll6h, AtrPeriod);
			var solRsi = Corendicators.ComputeRsi6h (solWinTrain, RsiPeriod);
			var btcSma200 = Corendicators.ComputeSma6h (btcWinTrain, BtcSmaPeriod);

			var solEma50 = Corendicators.ComputeEma6h (solAll6h, SolEmaFast);
			var solEma200 = Corendicators.ComputeEma6h (solAll6h, SolEmaSlow);

			var btcEma50 = Corendicators.ComputeEma6h (btcWinTrain, BtcEmaFast);
			var btcEma200 = Corendicators.ComputeEma6h (btcWinTrain, BtcEmaSlow);

			// Граница, до которой корректно покрываем baseline-exit последней 6h-свечой.
			var last6h = solAll6h[solAll6h.Count - 1];
			var maxExitUtc = last6h.OpenTimeUtc.AddHours (6);

			var minCfg = new MinMoveConfig ();
			var minState = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var rows = new List<DataRow> (solWinTrain.Count);

			for (int solIdx = 0; solIdx < solWinTrain.Count; solIdx++)
				{
				var c = solWinTrain[solIdx];
				DateTime openUtc = c.OpenTimeUtc;

				using var _ = Infra.Causality.CausalityGuard.Begin (
					"RowBuilder.BuildRowsDaily(day)",
					openUtc
				);

				var ny = TimeZoneInfo.ConvertTimeFromUtc (openUtc, nyTz);

				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				DateTime exitUtc = Windowing.ComputeBaselineExitUtc (openUtc, nyTz);

				if (exitUtc >= maxExitUtc)
					continue;

				var exitCandle = Find6hCandleContainingTimeSorted (solAll6h, exitUtc);
				if (exitCandle == null)
					{
					throw new InvalidOperationException (
						$"[RowBuilder] no 6h candle covering baseline exit {exitUtc:O} for entry {openUtc:O}");
					}

				if (!btcIndexByOpen.TryGetValue (openUtc, out int btcIdx))
					throw new InvalidOperationException ($"[RowBuilder] no BTC 6h candle matching SOL candle at {openUtc:O}.");

				if (!paxgIndexByOpen.TryGetValue (openUtc, out int gIdx))
					throw new InvalidOperationException ($"[RowBuilder] no PAXG candle for SOL entry {openUtc:O}.");

				// Недостаточно истории для ретурнов — пропускаем самые ранние дни.
				if (solIdx == 0 || btcIdx == 0)
					continue;

				double solClose = c.Close;
				double solCloseFwd = exitCandle.Close;

				if (solClose <= 0 || solCloseFwd <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive SOL close price at entry {openUtc:O} or exit {exitUtc:O}.");

				double solRet1 = Corendicators.Ret6h (solWinTrain, solIdx, 1);
				double solRet3 = Corendicators.Ret6h (solWinTrain, solIdx, 3);
				double solRet30 = Corendicators.Ret6h (solWinTrain, solIdx, 30);

				double btcRet1 = Corendicators.Ret6h (btcWinTrain, btcIdx, 1);
				double btcRet3 = Corendicators.Ret6h (btcWinTrain, btcIdx, 3);
				double btcRet30 = Corendicators.Ret6h (btcWinTrain, btcIdx, 30);

				if (double.IsNaN (solRet1) || double.IsNaN (solRet3) || double.IsNaN (solRet30) ||
					double.IsNaN (btcRet1) || double.IsNaN (btcRet3) || double.IsNaN (btcRet30))
					continue;

				double solBtcRet30 = solRet30 - btcRet30;

				double solEma50Val = Corendicators.FindNearest (solEma50, openUtc, 0.0);
				double solEma200Val = Corendicators.FindNearest (solEma200, openUtc, 0.0);
				double btcEma50Val = Corendicators.FindNearest (btcEma50, openUtc, 0.0);
				double btcEma200Val = Corendicators.FindNearest (btcEma200, openUtc, 0.0);

				double solAboveEma50 = solEma50Val > 0 && solClose > 0
					? (solClose - solEma50Val) / solEma50Val
					: 0.0;

				double solEma50vs200 = solEma200Val > 0
					? (solEma50Val - solEma200Val) / solEma200Val
					: 0.0;

				double btcEma50vs200 = btcEma200Val > 0
					? (btcEma50Val - btcEma200Val) / btcEma200Val
					: 0.0;

				double fng = Corendicators.PickNearestFng (fngHistory, openUtc.Date);
				double fngNorm = (fng - 50.0) / 50.0;

				double dxyChg30 = Corendicators.GetDxyChange30 (dxySeries, openUtc.Date);
				dxyChg30 = Math.Clamp (dxyChg30, -0.03, 0.03);

				int g30 = gIdx - 30;
				if (g30 < 0)
					continue;

				double gNow = paxgWinTrain[gIdx].Close;
				double gPast = paxgWinTrain[g30].Close;

				if (gNow <= 0 || gPast <= 0)
					throw new InvalidOperationException ($"[RowBuilder] invalid PAXG close prices for 30-day change at {openUtc:O}.");

				double goldChg30 = gNow / gPast - 1.0;

				if (!btcSma200.TryGetValue (openUtc, out double sma200))
					continue;

				if (sma200 <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive BTC 200SMA at {openUtc:O}.");

				double btcClose = btcWinTrain[btcIdx].Close;
				double btcVs200 = (btcClose - sma200) / sma200;

				if (!solRsi.TryGetValue (openUtc, out double solRsiVal))
					continue;

				double solRsiCentered = solRsiVal - 50.0;
				double rsiSlope3 = Corendicators.GetRsiSlope6h (solRsi, openUtc, 3);

				double gapBtcSol1 = btcRet1 - solRet1;
				double gapBtcSol3 = btcRet3 - solRet3;

				double dynVol = Corendicators.ComputeDynVol6h (solWinTrain, solIdx, 10);
				if (dynVol <= 0)
					throw new InvalidOperationException ($"[RowBuilder] dynVol is non-positive at {openUtc:O} (solIdx={solIdx}).");

				double atrAbs = Corendicators.FindNearest (solAtr, openUtc, 0.0);
				if (atrAbs <= 0)
					throw new InvalidOperationException ($"[RowBuilder] ATR is non-positive at {openUtc:O}.");

				double atrPct = atrAbs / solClose;

				bool isDownRegime = solRet30 < DownSol30Thresh || btcRet30 < DownBtc30Thresh;

				double solFwd1 = solCloseFwd / solClose - 1.0;

				var mm = MinMoveEngine.ComputeAdaptive (
					asOfUtc: openUtc,
					regimeDown: isDownRegime,
					atrPct: atrPct,
					dynVol: dynVol,
					historyRows: rows,
					cfg: minCfg,
					state: minState);

				double minMove = mm.MinMove;

				// extraDaily пока оставляем как есть (подписано LEGACY в твоём коде).
				double funding = 0.0, oi = 0.0;
				if (extraDaily != null && extraDaily.TryGetValue (openUtc.Date, out var ex))
					{
					funding = ex.Funding;
					oi = ex.OI;
					}

				int firstPassDir;
				DateTime? firstPassTimeUtc;
				double pathUp, pathDown;

				int pathLabel = PathLabeler.AssignLabel (
					entryUtc: openUtc,
					entryPrice: solClose,
					minMove: minMove,
					minutes: solAll1m,
					out firstPassDir,
					out firstPassTimeUtc,
					out pathUp,
					out pathDown);

				bool factMicroUp = false;
				bool factMicroDown = false;

				if (pathLabel == 1)
					{
					if (pathUp > Math.Abs (pathDown) + 0.001)
						factMicroUp = true;
					else if (Math.Abs (pathDown) > pathUp + 0.001)
						factMicroDown = true;
					}

				int hardRegime = Math.Abs (solRet30) > 0.10 || atrPct > 0.035 ? 2 : 1;
				bool isMorning = Windowing.IsNyMorning (openUtc, nyTz);

				double altFrac6h = double.NaN;
				double altFrac24h = double.NaN;
				double altMedian24h = double.NaN;
				int altCount = 0;
				bool altReliable = false;

				var feats = new List<double>
					{
					solRet30,
					btcRet30,
					solBtcRet30,
					solRet1,
					solRet3,
					btcRet1,
					btcRet3,
					fngNorm,
					dxyChg30,
					goldChg30,
					btcVs200,
					solRsiCentered / 100.0,
					rsiSlope3 / 100.0,
					gapBtcSol1,
					gapBtcSol3,
					isDownRegime ? 1.0 : 0.0,
					atrPct,
					dynVol,
					hardRegime == 2 ? 1.0 : 0.0,
					solAboveEma50,
					solEma50vs200,
					btcEma50vs200
					};

				// ==== ТЕСТОВЫЕ УТЕЧКИ (только диагностика) ====
				if (RowBuilderLeakageFlags.EnableRowBuilderLeakSolFwd1)
					{
					if (feats.Count > 0)
						feats[0] = solFwd1;
					}

				if (RowBuilderLeakageFlags.EnableRowBuilderLeakSingleMinutePeek)
					{
					// В debug-режиме можно оставить линейный поиск.
					var future1m = solAll1m.FirstOrDefault (m => m.OpenTimeUtc > exitUtc);
					if (future1m != null)
						{
						double futureClose = future1m.Close;

						if (feats.Count > 1) feats[1] = futureClose;
						else if (feats.Count == 1) feats[0] = futureClose;
						}
					}

				var row = new DataRow
					{
					Date = openUtc,
					Features = feats.ToArray (),
					Label = pathLabel,

					RegimeDown = isDownRegime,
					IsMorning = isMorning,

					SolRet30 = solRet30,
					BtcRet30 = btcRet30,
					SolRet1 = solRet1,
					SolRet3 = solRet3,
					BtcRet1 = btcRet1,
					BtcRet3 = btcRet3,

					Fng = fng,
					DxyChg30 = dxyChg30,
					GoldChg30 = goldChg30,
					BtcVs200 = btcVs200,
					SolRsiCentered = solRsiCentered,
					RsiSlope3 = rsiSlope3,

					AtrPct = atrPct,
					DynVol = dynVol,
					MinMove = minMove,

					SolFwd1 = solFwd1,

					AltFracPos6h = altFrac6h,
					AltFracPos24h = altFrac24h,
					AltMedian24h = altMedian24h,
					AltCount = altCount,
					AltReliable = altReliable,

					FactMicroUp = factMicroUp,
					FactMicroDown = factMicroDown,

					HardRegime = hardRegime,

					PathFirstPassDir = firstPassDir,
					PathFirstPassTimeUtc = firstPassTimeUtc,
					PathReachedUpPct = pathUp,
					PathReachedDownPct = pathDown,

					SolEma50 = solEma50Val,
					SolEma200 = solEma200Val,
					BtcEma50 = btcEma50Val,
					BtcEma200 = btcEma200Val,
					SolEma50vs200 = solEma50vs200,
					BtcEma50vs200 = btcEma50vs200,
					};

				rows.Add (row);
				}

			return rows;
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

		/// <summary>
		/// Быстрый поиск 6h-свечи, покрывающей targetUtc.
		/// Требование: all отсортирован строго по OpenTimeUtc.
		/// Интервал как раньше: [OpenTimeUtc; nextOpenTimeUtc) либо [OpenTimeUtc; OpenTimeUtc+6h] для последней.
		/// </summary>
		private static Candle6h? Find6hCandleContainingTimeSorted ( IReadOnlyList<Candle6h> all, DateTime targetUtc )
			{
			if (all == null || all.Count == 0)
				return null;

			int idx = UpperBoundOpenTimeUtc (all, targetUtc) - 1;
			if (idx < 0)
				return null;

			var cur = all[idx];

			DateTime start = cur.OpenTimeUtc;
			DateTime end = idx + 1 < all.Count
				? all[idx + 1].OpenTimeUtc
				: cur.OpenTimeUtc.AddHours (6);

			if (targetUtc >= start && targetUtc < end)
				return cur;

			return null;
			}

		/// <summary>
		/// UpperBound по OpenTimeUtc: первый индекс i, где OpenTimeUtc > t.
		/// </summary>
		private static int UpperBoundOpenTimeUtc ( IReadOnlyList<Candle6h> all, DateTime t )
			{
			int lo = 0;
			int hi = all.Count;

			while (lo < hi)
				{
				int mid = lo + ((hi - lo) >> 1);
				if (all[mid].OpenTimeUtc <= t)
					lo = mid + 1;
				else
					hi = mid;
				}

			return lo;
			}
		}
	}
