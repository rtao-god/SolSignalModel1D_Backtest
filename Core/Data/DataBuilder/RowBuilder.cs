using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public static class RowBuilder
		{
		private const int AtrPeriod = 14;
		private const int RsiPeriod = 14;
		private const int BtcSmaPeriod = 200;

		// таргет
		private const double TargetHorizonHours = 24.0;

		// базовый minMove
		private const double DailyMinMoveFloor = 0.0075;
		private const double DailyMinMoveCap = 0.036;
		private const double DailyMinMoveAtrMul = 1.10;
		private const double DailyMinMoveVolMul = 1.10;

		// старый “даун”-режим
		private const double DownSol30Thresh = -0.07;
		private const double DownBtc30Thresh = -0.05;

		// ограничения для месячного фактора
		private const double MonthlyCapMin = 0.75;  // не давать месяцу слишком занизить
		private const double MonthlyCapMax = 1.35;  // и слишком завысить

		private const int SolEmaFast = 50;
		private const int SolEmaSlow = 200;
		private const int BtcEmaFast = 50;
		private const int BtcEmaSlow = 200;


		public static List<DataRow> BuildRowsDaily (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			Dictionary<DateTime, int> fngHistory,
			Dictionary<DateTime, double> dxySeries,
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily,
			TimeZoneInfo nyTz
		)
			{
			// предрасчёты
			var solAtr = Indicators.ComputeAtr6h (solAll6h, AtrPeriod);
			var solRsi = Indicators.ComputeRsi6h (solWinTrain, RsiPeriod);
			var btcSma200 = Indicators.ComputeSma6h (btcWinTrain, BtcSmaPeriod);

			// EMA по SOL считаем по всем 6h, как и ATR — чтобы были непрерывные даты
			var solEma50 = Indicators.ComputeEma6h (solAll6h, SolEmaFast);
			var solEma200 = Indicators.ComputeEma6h (solAll6h, SolEmaSlow);

			// по BTC у нас только btcWinTrain, но этого хватает для фоновой фичи
			var btcEma50 = Indicators.ComputeEma6h (btcWinTrain, BtcEmaFast);
			var btcEma200 = Indicators.ComputeEma6h (btcWinTrain, BtcEmaSlow);

			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			var rows = new List<DataRow> ();

			foreach (var c in solWinTrain)
				{
				DateTime openUtc = c.OpenTimeUtc;
				var ny = TimeZoneInfo.ConvertTimeFromUtc (openUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				// таргет
				DateTime targetUtc = openUtc.AddHours (TargetHorizonHours);
				if (!sol6hDict.TryGetValue (targetUtc, out var fwdCandle))
					continue;

				var targetNy = TimeZoneInfo.ConvertTimeFromUtc (targetUtc, nyTz);
				if (targetNy.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				// синхронизация BTC
				var btcC = btcWinTrain.FirstOrDefault (x => x.OpenTimeUtc == openUtc);
				if (btcC == null) continue;

				int solIdx = solWinTrain.IndexOf (c);
				int btcIdx = btcWinTrain.IndexOf (btcC);
				if (solIdx <= 0 || btcIdx <= 0) continue;

				double solClose = c.Close;
				double solCloseFwd = fwdCandle.Close;
				if (solClose <= 0 || solCloseFwd <= 0) continue;

				// ретурны
				double solRet1 = Indicators.Ret6h (solWinTrain, solIdx, 1);
				double solRet3 = Indicators.Ret6h (solWinTrain, solIdx, 3);
				double solRet30 = Indicators.Ret6h (solWinTrain, solIdx, 30);

				double btcRet1 = Indicators.Ret6h (btcWinTrain, btcIdx, 1);
				double btcRet3 = Indicators.Ret6h (btcWinTrain, btcIdx, 3);
				double btcRet30 = Indicators.Ret6h (btcWinTrain, btcIdx, 30);

				// EMA по SOL
				double solEma50Val = Indicators.FindNearest (solEma50, openUtc, 0.0);
				double solEma200Val = Indicators.FindNearest (solEma200, openUtc, 0.0);

				// EMA по BTC
				double btcEma50Val = Indicators.FindNearest (btcEma50, openUtc, 0.0);
				double btcEma200Val = Indicators.FindNearest (btcEma200, openUtc, 0.0);

				// нормализованные/производные
				double solAboveEma50 = (solEma50Val > 0 && solClose > 0)
					? (solClose - solEma50Val) / solEma50Val
					: 0.0;

				double solEmaSlope = 0.0; // можно добавить позже из соседних свечей

				double solEma50vs200 = (solEma200Val > 0)
					? (solEma50Val - solEma200Val) / solEma200Val
					: 0.0;

				double btcEma50vs200 = (btcEma200Val > 0)
					? (btcEma50Val - btcEma200Val) / btcEma200Val
					: 0.0;

				if (double.IsNaN (solRet1) || double.IsNaN (solRet3) || double.IsNaN (solRet30) ||
					double.IsNaN (btcRet1) || double.IsNaN (btcRet3) || double.IsNaN (btcRet30))
					continue;

				double solBtcRet30 = solRet30 - btcRet30;

				// FNG
				int fng = 50;
				if (fngHistory != null && fngHistory.Count > 0)
					fng = Indicators.PickNearestFng (fngHistory, openUtc.Date);
				double fngNorm = (fng - 50.0) / 50.0;

				// DXY
				double dxyChg30 = 0.0;
				if (dxySeries != null && dxySeries.Count > 0)
					{
					dxyChg30 = Indicators.GetDxyChange30 (dxySeries, openUtc.Date);
					dxyChg30 = Math.Clamp (dxyChg30, -0.03, 0.03);
					}

				// GOLD
				double goldChg30 = 0.0;
				if (paxgWinTrain.Count > 0)
					{
					var gC = paxgWinTrain.FirstOrDefault (x => x.OpenTimeUtc == openUtc);
					if (gC != null)
						{
						int gIdx = paxgWinTrain.IndexOf (gC);
						int g30 = gIdx - 30;
						if (g30 >= 0)
							{
							double gNow = gC.Close;
							double gPast = paxgWinTrain[g30].Close;
							if (gNow > 0 && gPast > 0)
								goldChg30 = gNow / gPast - 1.0;
							}
						}
					}

				// BTC vs 200
				double btcVs200 = 0.0;
				if (btcSma200.TryGetValue (openUtc, out double sma200) && sma200 > 0)
					btcVs200 = (btcC.Close - sma200) / sma200;

				// RSI
				double solRsiVal = solRsi.TryGetValue (openUtc, out double rsiTmp) ? rsiTmp : 50.0;
				double solRsiCentered = solRsiVal - 50.0;
				double rsiSlope3 = Indicators.GetRsiSlope6h (solRsi, openUtc, 3);

				// gap BTC-SOL
				double gapBtcSol1 = btcRet1 - solRet1;
				double gapBtcSol3 = btcRet3 - solRet3;

				// вола
				double dynVol = Indicators.ComputeDynVol6h (solWinTrain, solIdx, 10);
				if (dynVol <= 0) dynVol = 0.004;

				// ATR
				double atrAbs = Indicators.FindNearest (solAtr, openUtc, 0.0);
				double atrPct = atrAbs > 0 && solClose > 0 ? atrAbs / solClose : 0.0;

				// ====== адаптивный minMove: базовый * месячный фактор ======
				double baseMinMove = Math.Max (
					DailyMinMoveFloor,
					Math.Max (atrPct * DailyMinMoveAtrMul, dynVol * DailyMinMoveVolMul)
				);
				double monthFactor = ComputeMonthlyMinMoveFactor (openUtc, solAll6h, 1.0);
				monthFactor = Math.Clamp (monthFactor, MonthlyCapMin, MonthlyCapMax);
				double minMove = baseMinMove * monthFactor;
				minMove = Math.Min (minMove, DailyMinMoveCap);

				// таргет
				double solFwd1 = solCloseFwd / solClose - 1.0;
				int label;
				if (solFwd1 <= -minMove) label = 0;
				else if (solFwd1 >= +minMove) label = 2;
				else label = 1;

				// старый режим
				bool isDownRegime = solRet30 < DownSol30Thresh || btcRet30 < DownBtc30Thresh;

				// extra
				double funding = 0.0;
				double oi = 0.0;
				if (extraDaily != null && extraDaily.TryGetValue (openUtc.Date, out var ex))
					{
					funding = ex.Funding;
					oi = ex.OI;
					}

				bool isMorning = Windowing.IsNyMorning (openUtc, nyTz);

				// alt-заглушка
				double altFrac6h = 1.0;
				double altFrac24h = 1.0;
				double altMedian24h = 0.02;
				int altCount = 4;
				bool altReliable = true;

				// жёсткий режим
				int hardRegime = Math.Abs (solRet30) > 0.10 || atrPct > 0.035 ? 2 : 1;

				// фичи
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
					funding,
					oi / 1_000_000.0,
					// стрессовый режим как бинарная фича
					hardRegime == 2 ? 1.0 : 0.0,

					 // EMA-блок
					solAboveEma50,     // насколько SOL выше/ниже своей EMA50
					solEma50vs200,     // быстрый EMA vs медленный по SOL
					btcEma50vs200      // фон: быстрый EMA vs медленный по BTC
				};

				rows.Add (new DataRow
					{
					Date = openUtc,
					Features = feats.ToArray (),
					Label = label,
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

					FactMicroUp = Math.Abs (solFwd1) < minMove && solFwd1 >= minMove * 0.1,
					FactMicroDown = Math.Abs (solFwd1) < minMove && solFwd1 <= -minMove * 0.1,

					HardRegime = hardRegime,

					// EMA
					SolEma50 = solEma50Val,
					SolEma200 = solEma200Val,
					BtcEma50 = btcEma50Val,
					BtcEma200 = btcEma200Val,
					SolEma50vs200 = solEma50vs200,
					BtcEma50vs200 = btcEma50vs200,
					});
				}

			return rows;
			}

		/// <summary>
		/// Очень грубый фактор "насколько в этом месяце была вола выше/ниже"
		/// </summary>
		private static double ComputeMonthlyMinMoveFactor ( DateTime asOf, List<Candle6h> all, double defaultFactor )
			{
			// берём последние ~30 дней до asOf
			DateTime from = asOf.AddDays (-30);
			var seg = all.Where (c => c.OpenTimeUtc >= from && c.OpenTimeUtc <= asOf).ToList ();
			if (seg.Count < 10)
				return defaultFactor;

			// считаем средний диапазон
			double sumRange = 0.0;
			int cnt = 0;
			foreach (var c in seg)
				{
				double range = c.High - c.Low;
				if (range > 0)
					{
					sumRange += range / c.Close; // в процентах от цены
					cnt++;
					}
				}

			if (cnt == 0)
				return defaultFactor;

			double monthVol = sumRange / cnt; // типа средний дневной %-диапазон за месяц

			// 0.025 считаем "нормой", масштабируем
			double factor = monthVol / 0.025;
			return factor;
			}

		/// <summary>
		/// Жёсткая классификация режима по воле/движению.
		/// </summary>
		private static int ComputeHardRegime ( double atrPct, double dynVol, double solRet30, double btcRet30 )
			{
			// стресс: вола реально высокая или месяц уже сильно уехал
			bool stress =
				atrPct > 0.035 ||         // 3.5% ATR
				dynVol > 0.028 ||         // 2.8% short-term vol
				solRet30 < -0.08 ||       // sol за месяц сильно вниз
				solRet30 > 0.11 ||        // или сильно вверх
				btcRet30 < -0.06;         // или биток тоже вниз

			if (stress)
				return 2;

			// low: тихо и не ехали
			bool low =
				atrPct < 0.018 &&
				dynVol < 0.012 &&
				Math.Abs (solRet30) < 0.03;

			if (low)
				return 0;

			return 1; // норм
			}
		}
	}