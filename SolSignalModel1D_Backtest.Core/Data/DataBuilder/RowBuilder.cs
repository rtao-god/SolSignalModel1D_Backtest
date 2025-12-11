using SolSignalModel1D_Backtest.Core.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder.Diagnostics;
using SolSignalModel1D_Backtest.Core.Data.Indicators;

namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder
	{
	/// <summary>
	/// Построитель дневных строк DataRow из 6h/1m свечей и дневных индикаторов.
	/// Делает:
	/// - расчёт индикаторов (RSI, ATR, EMA, BTC 200SMA, FNG, DXY, GOLD);
	/// - определение режима (NORMAL/DOWN);
	/// - каузальный adaptive MinMove;
	/// - path-based разметку по 1m (PathLabeler + micro-факт);
	/// - таргет SolFwd1 по базовому горизонту выхода (NY→следующее NY утро).
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
		/// Старый удобный вход без 1m. Сейчас 1m обязательны,
		/// поэтому этот оверлоад просто прокидывает null и улетает
		/// в InvalidOperationException внутри нового метода.
		/// Оставлен для совместимости по сигнатурам.
		/// </summary>
		public static List<DataRow> BuildRowsDaily (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			Dictionary<DateTime, double> fngHistory,
			Dictionary<DateTime, double> dxySeries,
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily,
			TimeZoneInfo nyTz )
			{
			return BuildRowsDaily (
				solWinTrain,
				btcWinTrain,
				paxgWinTrain,
				solAll6h,
				solAll1m: null,
				fngHistory: fngHistory,
				dxySeries: dxySeries,
				extraDaily: extraDaily,
				nyTz: nyTz);
			}

		/// <summary>
		/// Основной вариант: 6h + 1m + адаптивный minMove через MinMoveEngine.
		/// 1m ОБЯЗАТЕЛЬНЫ. Если их нет — кидается InvalidOperationException.
		/// Горизонт выхода SolFwd1: baseline exit → следующая рабочая NY-утренняя
		/// граница 08:00 локального времени (с учётом DST), минус 2 минуты.
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
			if (solAll1m == null || solAll1m.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] solAll1m is required for path-based labels and MinMoveEngine.");

			// Индикаторы по 6h	
			var solAtr = Indicators.ComputeAtr6h (solAll6h, AtrPeriod);
			var solRsi = Indicators.ComputeRsi6h (solWinTrain, RsiPeriod);
			var btcSma200 = Indicators.ComputeSma6h (btcWinTrain, BtcSmaPeriod);

			var solEma50 = Indicators.ComputeEma6h (solAll6h, SolEmaFast);
			var solEma200 = Indicators.ComputeEma6h (solAll6h, SolEmaSlow);

			var btcEma50 = Indicators.ComputeEma6h (btcWinTrain, BtcEmaFast);
			var btcEma200 = Indicators.ComputeEma6h (btcWinTrain, BtcEmaSlow);

			// Быстрый доступ к 6h SOL по времени открытия (пока не используется, оставлено для совместимости)
			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			// Отсортированные 1m-свечи для path-разметки
			var sol1mSorted = solAll1m;

			// Отсортированные 6h-свечи и максимум по exit-времени,
			// который вообще можно корректно покрыть последней 6h-свечой.
			var sol6hSorted = solAll6h;

			if (sol6hSorted.Count == 0)
				throw new InvalidOperationException ("[RowBuilder] solAll6h is empty.");

			var last6h = sol6hSorted[sol6hSorted.Count - 1];

			// Последняя 6h-свеча покрывает интервал [last6h.OpenTimeUtc; last6h.OpenTimeUtc + 6h).
			// Любой baseline exit позже maxExitUtc честно покрыть нельзя → такие дни скипаются.
			var maxExitUtc = last6h.OpenTimeUtc.AddHours (6);

			// Конфиг/состояние адаптивного minMove (каузальное накопление истории)
			var minCfg = new MinMoveConfig ();
			var minState = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var rows = new List<DataRow> ();

			foreach (var c in solWinTrain)
				{
				DateTime openUtc = c.OpenTimeUtc;
				var ny = TimeZoneInfo.ConvertTimeFromUtc (openUtc, nyTz);

				// Сигналы и строки строятся только для будних дней.
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				// Базовый выход: следующая рабочая NY-утренняя граница 08:00 (минус 2 минуты).
				DateTime exitUtc = Windowing.ComputeBaselineExitUtc (openUtc, nyTz);

				// Если baseline exit вылезает за конец последней доступной 6h-свечи,
				// честно посчитать SolFwd1 нельзя → этот день скипаем.
				if (exitUtc >= maxExitUtc)
					{
					// Никаких исключений здесь не кидаем, это естественный край данных.
					continue;
					}

				// 6h-свеча, которая покрывает момент выхода.
				var exitCandle = Find6hCandleContainingTime (solAll6h, exitUtc);
				if (exitCandle == null)
					{
					// Это уже реальная дыра в 6h-серии, а не естественный край.
					throw new InvalidOperationException (
						$"[RowBuilder] no 6h candle covering baseline exit {exitUtc:O} for entry {openUtc:O}");
					}

				var btcC = btcWinTrain.FirstOrDefault (x => x.OpenTimeUtc == openUtc);
				if (btcC == null)
					throw new InvalidOperationException ($"[RowBuilder] no BTC 6h candle matching SOL candle at {openUtc:O}.");

				int solIdx = solWinTrain.IndexOf (c);
				int btcIdx = btcWinTrain.IndexOf (btcC);

				if (solIdx < 0)
					throw new InvalidOperationException ($"[RowBuilder] SOL candle not found in solWinTrain at {openUtc:O}.");
				if (btcIdx < 0)
					throw new InvalidOperationException ($"[RowBuilder] BTC candle not found in btcWinTrain at {openUtc:O}.");

				// Недостаточно истории для ретурнов — пропускаем самые ранние дни
				if (solIdx == 0 || btcIdx == 0)
					continue;

				double solClose = c.Close;
				double solCloseFwd = exitCandle.Close;
				if (solClose <= 0 || solCloseFwd <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive SOL close price at entry {openUtc:O} or exit {exitUtc:O}.");

				// Ретурны SOL/BTC на разных горизонтах (по 6h-окнам)
				double solRet1 = Indicators.Ret6h (solWinTrain, solIdx, 1);
				double solRet3 = Indicators.Ret6h (solWinTrain, solIdx, 3);
				double solRet30 = Indicators.Ret6h (solWinTrain, solIdx, 30);

				double btcRet1 = Indicators.Ret6h (btcWinTrain, btcIdx, 1);
				double btcRet3 = Indicators.Ret6h (btcWinTrain, btcIdx, 3);
				double btcRet30 = Indicators.Ret6h (btcWinTrain, btcIdx, 30);

				if (double.IsNaN (solRet1) || double.IsNaN (solRet3) || double.IsNaN (solRet30) ||
					double.IsNaN (btcRet1) || double.IsNaN (btcRet3) || double.IsNaN (btcRet30))
					continue;

				double solBtcRet30 = solRet30 - btcRet30;

				// EMA-блок по SOL/BTC (50/200) и производные фичи
				double solEma50Val = Indicators.FindNearest (solEma50, openUtc, 0.0);
				double solEma200Val = Indicators.FindNearest (solEma200, openUtc, 0.0);
				double btcEma50Val = Indicators.FindNearest (btcEma50, openUtc, 0.0);
				double btcEma200Val = Indicators.FindNearest (btcEma200, openUtc, 0.0);

				double solAboveEma50 = solEma50Val > 0 && solClose > 0
					? (solClose - solEma50Val) / solEma50Val
					: 0.0;
				double solEma50vs200 = solEma200Val > 0
					? (solEma50Val - solEma200Val) / solEma200Val
					: 0.0;
				double btcEma50vs200 = btcEma200Val > 0
					? (btcEma50Val - btcEma200Val) / btcEma200Val
					: 0.0;

				// Fear & Greed: без истории — это уже ошибка входных данных
				if (fngHistory == null || fngHistory.Count == 0)
					throw new InvalidOperationException ("[RowBuilder] fngHistory is null or empty.");

				double fng = Indicators.PickNearestFng (fngHistory, openUtc.Date);
				double fngNorm = (fng - 50.0) / 50.0;

				// DXY (сжатый 30-дневный change) — без ряда DXY тоже считаем ошибкой
				if (dxySeries == null || dxySeries.Count == 0)
					throw new InvalidOperationException ("[RowBuilder] dxySeries is null or empty.");

				double dxyChg30 = Indicators.GetDxyChange30 (dxySeries, openUtc.Date);
				dxyChg30 = Math.Clamp (dxyChg30, -0.03, 0.03);

				// GOLD через PAXG (30-дневный change)
				if (paxgWinTrain == null || paxgWinTrain.Count == 0)
					throw new InvalidOperationException ("[RowBuilder] paxgWinTrain is null or empty.");

				var gC = paxgWinTrain.FirstOrDefault (x => x.OpenTimeUtc == openUtc);
				if (gC == null)
					throw new InvalidOperationException ($"[RowBuilder] no PAXG candle for SOL entry {openUtc:O}.");

				int gIdx = paxgWinTrain.IndexOf (gC);
				int g30 = gIdx - 30;
				if (g30 < 0)
					{
					// Недостаточно истории по золоту для 30-дневного изменения — скипаем день.
					continue;
					}

				double gNow = gC.Close;
				double gPast = paxgWinTrain[g30].Close;
				if (gNow <= 0 || gPast <= 0)
					throw new InvalidOperationException ($"[RowBuilder] invalid PAXG close prices for 30-day change at {openUtc:O}.");

				double goldChg30 = gNow / gPast - 1.0;

				// BTC vs 200SMA (позиция BTC относительно долгосрочной средней)
				if (!btcSma200.TryGetValue (openUtc, out double sma200))
					{
					// Недостаточно истории для 200SMA — не подставляем заглушку, просто не берём день в выборку.
					continue;
					}
				if (sma200 <= 0)
					throw new InvalidOperationException ($"[RowBuilder] non-positive BTC 200SMA at {openUtc:O}.");

				double btcVs200 = (btcC.Close - sma200) / sma200;

				// RSI-блок (центрированный и наклон за 3 шага) — без значения RSI день не берём
				if (!solRsi.TryGetValue (openUtc, out double solRsiVal))
					{
					// Недостаточно истории для RSI — скипаем день.
					continue;
					}
				double solRsiCentered = solRsiVal - 50.0;
				double rsiSlope3 = Indicators.GetRsiSlope6h (solRsi, openUtc, 3);

				// GAP между BTC и SOL на коротких горизонтах
				double gapBtcSol1 = btcRet1 - solRet1;
				double gapBtcSol3 = btcRet3 - solRet3;

				// Волатильность: dynVol + ATR (в процентах)
				double dynVol = Indicators.ComputeDynVol6h (solWinTrain, solIdx, 10);
				if (dynVol <= 0)
					throw new InvalidOperationException ($"[RowBuilder] dynVol is non-positive at {openUtc:O} (solIdx={solIdx}).");

				double atrAbs = Indicators.FindNearest (solAtr, openUtc, 0.0);
				if (atrAbs <= 0)
					throw new InvalidOperationException ($"[RowBuilder] ATR is non-positive at {openUtc:O}.");

				double atrPct = atrAbs / solClose;

				// Режим рынка (DOWN / NORMAL) по Sol/BTC 30d
				bool isDownRegime = solRet30 < DownSol30Thresh || btcRet30 < DownBtc30Thresh;

				// Таргет по close на базовом горизонте выхода (entry → baseline exit)
				double solFwd1 = solCloseFwd / solClose - 1.0;

				// Адаптивный MinMove (каузально, на основе уже построенной истории rows)
				var mm = MinMoveEngine.ComputeAdaptive (
					asOfUtc: openUtc,
					regimeDown: isDownRegime,
					atrPct: atrPct,
					dynVol: dynVol,
					historyRows: rows,
					cfg: minCfg,
					state: minState);

				double minMove = mm.MinMove;

				// Дневные extra-поля (funding / OI) (пока что LEGACY, поэтому отсутствие данных не считаем ошибкой)
				double funding = 0.0, oi = 0.0;
				if (extraDaily != null && extraDaily.TryGetValue (openUtc.Date, out var ex))
					{
					funding = ex.Funding;
					oi = ex.OI;
					}

				// Path-based разметка по 1m (полный путь от entry, minMove-ориентированный)
				int pathLabel;
				int firstPassDir;
				DateTime? firstPassTimeUtc;
				double pathUp, pathDown;

				pathLabel = PathLabeler.AssignLabel (
					entryUtc: openUtc,
					entryPrice: solClose,
					minMove: minMove,
					minutes: sol1mSorted,
					out firstPassDir,
					out firstPassTimeUtc,
					out pathUp,
					out pathDown);

				// Micro-факты строго через pathUp/pathDown только для боковика (label=1)
				bool factMicroUp = false;
				bool factMicroDown = false;
				if (pathLabel == 1)
					{
					if (pathUp > Math.Abs (pathDown) + 0.001)
						factMicroUp = true;
					else if (Math.Abs (pathDown) > pathUp + 0.001)
						factMicroDown = true;
					}

				// Жёсткий режим (stress regime) по SolRet30/ATR
				int hardRegime = Math.Abs (solRet30) > 0.10 || atrPct > 0.035 ? 2 : 1;

				// Флаг утреннего NY-окна (для фильтрации сигналов)
				bool isMorning = Windowing.IsNyMorning (openUtc, nyTz);

				// Alt-метрики пока не реализованы: явно помечаем отсутствующие данные, без "красивых" заглушек
				double altFrac6h = double.NaN;
				double altFrac24h = double.NaN;
				double altMedian24h = double.NaN;
				int altCount = 0;
				bool altReliable = false;

				// Вектор фич для ML-модели
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
					//funding, пока что не используем
					//oi / 1_000_000.0, пока что не используем
                    // стресс как бинарная фича 
                    hardRegime == 2 ? 1.0 : 0.0,
                    // EMA-блок
                    solAboveEma50,
					solEma50vs200,
					btcEma50vs200
				};

				// ==== ТЕСТОВЫЕ УТЕЧКИ (используются только в диагностиках) ====

				// 1) Хак через SolFwd1 (заведомо future-зависимая величина).
				if (RowBuilderLeakageFlags.EnableRowBuilderLeakSolFwd1)
					{
					if (feats.Count > 0)
						{
						// Переписываем первый признак таргетом SolFwd1.
						// Это грубая утечка, которая должна ловиться как по RowBuilder-тестам,
						// так и по self-check'ам RowFeatureLeakageChecks.
						feats[0] = solFwd1;
						}
					}

				// 2) Хак через минутный future-peek:
				// подмешиваем цену первой 1m-свечи строго ПОСЛЕ baseline-exit.
				if (RowBuilderLeakageFlags.EnableRowBuilderLeakSingleMinutePeek)
					{
					// sol1mSorted содержит полную минутную историю по SOL.
					// Ищем первую свечу после exitUtc. Для debug-режима достаточно линейного поиска.
					var future1m = sol1mSorted.FirstOrDefault (m => m.OpenTimeUtc > exitUtc);

					if (future1m != null)
						{
						double futureClose = future1m.Close;

						// если фич хотя бы две — кладём во второй признак,
						// чтобы не пересекаться с SolFwd1-хаком; иначе в первый.
						if (feats.Count > 1)
							{
							feats[1] = futureClose;
							}
						else if (feats.Count == 1)
							{
							feats[0] = futureClose;
							}
						}
					}

				var row = new DataRow
					{
					// Время входа (UTC, 6h-окно)
					Date = openUtc,

					// Фичи и таргет-класс (path-label)
					Features = feats.ToArray (),
					Label = pathLabel,

					// Режим/утро NY
					RegimeDown = isDownRegime,
					IsMorning = isMorning,

					// Основные ретурны
					SolRet30 = solRet30,
					BtcRet30 = btcRet30,
					SolRet1 = solRet1,
					SolRet3 = solRet3,
					BtcRet1 = btcRet1,
					BtcRet3 = btcRet3,

					// Макро-индикаторы
					Fng = fng,
					DxyChg30 = dxyChg30,
					GoldChg30 = goldChg30,
					BtcVs200 = btcVs200,
					SolRsiCentered = solRsiCentered,
					RsiSlope3 = rsiSlope3,

					// Вола/MinMove
					AtrPct = atrPct,
					DynVol = dynVol,
					MinMove = minMove,

					// Факт по close на baseline-горизонте
					SolFwd1 = solFwd1,

					// Alt-статистика: явное отсутствие (NaN/0) и AltReliable = false
					AltFracPos6h = altFrac6h,
					AltFracPos24h = altFrac24h,
					AltMedian24h = altMedian24h,
					AltCount = altCount,
					AltReliable = altReliable,

					// Micro-факты
					FactMicroUp = factMicroUp,
					FactMicroDown = factMicroDown,

					// Стресс-режим
					HardRegime = hardRegime,

					// Path-first метрики
					PathFirstPassDir = firstPassDir,
					PathFirstPassTimeUtc = firstPassTimeUtc,
					PathReachedUpPct = pathUp,
					PathReachedDownPct = pathDown,

					// EMA-raw + производные
					SolEma50 = solEma50Val,
					SolEma200 = solEma200Val,
					BtcEma50 = btcEma50Val,
					BtcEma200 = btcEma200Val,
					SolEma50vs200 = solEma50vs200,
					BtcEma50vs200 = btcEma50vs200,
					};

				// Важно: сначала считаются minMove и path-метрики по текущему дню,
				// потом строка добавляется в rows → для последующих дней она
				// видна как история, но текущий день себя не "видит".
				rows.Add (row);
				}

			return rows;
			}

		/// <summary>
		/// Ищет 6h-свечу, временной интервал которой покрывает указанный момент времени.
		/// Интервал считается [OpenTimeUtc; nextOpenTimeUtc) либо [OpenTimeUtc; OpenTimeUtc+6h],
		/// если следующая свеча отсутствует.
		/// </summary>
		private static Candle6h? Find6hCandleContainingTime ( List<Candle6h> all, DateTime targetUtc )
			{
			if (all == null || all.Count == 0)
				return null;

			var sorted = all.ToList ();
			for (int i = 0; i < all.Count; i++)
				{
				var cur = all[i];
				DateTime start = cur.OpenTimeUtc;
				DateTime end = i + 1 < all.Count
					? all[i + 1].OpenTimeUtc
					: cur.OpenTimeUtc.AddHours (6);

				if (targetUtc >= start && targetUtc < end)
					return cur;
				}
			return null;
			}
		}
	}
