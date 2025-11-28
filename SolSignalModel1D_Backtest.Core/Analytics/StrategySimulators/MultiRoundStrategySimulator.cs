using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
	{
	/// <summary>
	/// Multi-round стратегия:
	/// - торгует только по фильтру RSI 15m;
	/// - прогноз модели (PredLabel) не используется;
	/// - один день имеет фиксированное направление (LONG или SHORT);
	/// - внутри дня может быть до MaxTradesPerDay заходов;
	/// - размеры позиций считаются как доля капитала (StakeFraction);
	/// - прибыль выводится (WithdrawnProfit), убытки бьют по балансу;
	/// - на выходе возвращает MultiRoundStrategyResult с расширенной статистикой.
	/// </summary>
	public static class MultiRoundStrategySimulator
		{
		private const double StartBalanceUsd = 10_000.0;
		private const double StakeFraction = 0.30; // 30 % баланса на один трейд

		private const double SlPct = 0.01;   // 1 % стоп-лосс
		private const double TpPct = 0.015;  // 1.5 % тейк-профит

		private const int MaxTradesPerDay = 100;

		private const int RsiPeriod = 14;
		private const double RsiLongThreshold = 35.0;
		private const double RsiShortThreshold = 65.0;

		private enum TradeDirection
			{
			Long,
			Short
			}

		private enum ExitType
			{
			Tp,
			Sl,
			Time
			}

		/// <summary>
		/// Запуск стратегии.
		/// mornings:
		///   - дневные точки (DataRow), как в основном бэктесте;
		/// records:
		///   - OOS-записи PredictionRecord (используются только для фильтра по датам, сам прогноз не используется);
		/// candles1m:
		///   - полный ряд 1m свечей SOLUSDT.
		/// </summary>
		public static MultiRoundStrategyResult Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));

			var result = new MultiRoundStrategyResult ();

			if (candles1m.Count == 0)
				{
				// Пустые свечи — возвращаем пустой результат с начальными значениями.
				result.StartBalanceUsd = StartBalanceUsd;
				result.EndBalanceUsd = StartBalanceUsd;
				result.StakeStartUsd = StartBalanceUsd * StakeFraction;
				result.StakeMinUsd = result.StakeStartUsd;
				return result;
				}

			// Фильтр по OOS-дням: берем только те mornings, для которых есть PredictionRecord (по дате),
			// но сам PredLabel и прочее НЕ используются в стратегии.
			List<DataRow> days;
			if (records != null && records.Count > 0)
				{
				var oosDates = new HashSet<DateTime> (records.Select (r => r.DateUtc.Date));
				days = mornings
					.Where (d => oosDates.Contains (d.Date.Date))
					.OrderBy (d => d.Date)
					.ToList ();
				}
			else
				{
				days = mornings
					.OrderBy (d => d.Date)
					.ToList ();
				}

			if (days.Count == 0)
				{
				result.StartBalanceUsd = StartBalanceUsd;
				result.EndBalanceUsd = StartBalanceUsd;
				result.StakeStartUsd = StartBalanceUsd * StakeFraction;
				result.StakeMinUsd = result.StakeStartUsd;
				return result;
				}

			// 1m свечи сортируем один раз.
			var orderedCandles = candles1m
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			// Предрасчитываем RSI 15m.
			var rsiSeries = StrategyIndicatorUtils.Build15mRsi (orderedCandles, RsiPeriod);

			double balance = StartBalanceUsd;
			double withdrawn = 0.0;
			double balanceMin = balance;

			double stakeStartUsd = balance * StakeFraction;
			double stakeMinUsd = stakeStartUsd;

			int maxTradesInSingleDay = 0;
			int losingStreakCurrent = 0;
			int losingStreakMax = 0;

			var dayStatsList = result.DayStats;
			var equityCurve = result.EquityCurve;
			var weekdayBuckets = result.PnlByWeekday;
			var hourBuckets = result.PnlByEntryHourLocal;

			foreach (var day in days)
				{
				if (balance <= 0.0)
					{
					// Капитал обнулен — дальше не торгуем.
					break;
					}

				double dayPnl = 0.0;
				int dayTrades = 0;

				// RSI 15m на момент утренней точки (NY-окно), сам DataRow.Date в UTC.
				double? dayRsi = StrategyIndicatorUtils.GetRsiAt (rsiSeries, day.Date);
				if (!dayRsi.HasValue)
					{
					// Нет RSI — день пропускаем без торговли.
					continue;
					}

				TradeDirection direction;
				if (dayRsi.Value <= RsiLongThreshold)
					{
					direction = TradeDirection.Long;
					}
				else if (dayRsi.Value >= RsiShortThreshold)
					{
					direction = TradeDirection.Short;
					}
				else
					{
					// Нейтральная зона — не торгуем день.
					continue;
					}

				// Окно торговли: от утренней точки до baseline-выхода по тем же правилам, что и основной PnL.
				DateTime entryStartUtc = day.Date;
				DateTime exitUtc = Windowing.ComputeBaselineExitUtc (entryStartUtc, TimeZones.NewYork);

				int currentIndex = FindFirstCandleIndex (orderedCandles, entryStartUtc);
				if (currentIndex < 0)
					{
					// Нет минутных свечей после точки входа — день пропускаем.
					continue;
					}

				for (int tradeIndex = 0; tradeIndex < MaxTradesPerDay; tradeIndex++)
					{
					if (currentIndex >= orderedCandles.Count)
						break;

					var entryCandle = orderedCandles[currentIndex];
					if (entryCandle.OpenTimeUtc >= exitUtc)
						break;

					if (balance <= 0.0)
						break;

					double stakeUsd = balance * StakeFraction;
					if (stakeUsd <= 0.0)
						break;

					double entryPrice = entryCandle.Open;
					if (entryPrice <= 0.0)
						break;

					double qty = stakeUsd / entryPrice;

					double tpPrice;
					double slPrice;

					if (direction == TradeDirection.Long)
						{
						tpPrice = entryPrice * (1.0 + TpPct);
						slPrice = entryPrice * (1.0 - SlPct);
						}
					else
						{
						tpPrice = entryPrice * (1.0 - TpPct);
						slPrice = entryPrice * (1.0 + SlPct);
						}

					double exitPrice = entryPrice;
					ExitType exitType = ExitType.Time;

					int j = currentIndex;

					for (; j < orderedCandles.Count; j++)
						{
						var c = orderedCandles[j];

						if (c.OpenTimeUtc >= exitUtc)
							break;

						if (direction == TradeDirection.Long)
							{
							// Консервативно: сначала проверяем SL, потом TP.
							if (c.Low <= slPrice)
								{
								exitPrice = slPrice;
								exitType = ExitType.Sl;
								j++;
								break;
								}

							if (c.High >= tpPrice)
								{
								exitPrice = tpPrice;
								exitType = ExitType.Tp;
								j++;
								break;
								}
							}
						else
							{
							if (c.High >= slPrice)
								{
								exitPrice = slPrice;
								exitType = ExitType.Sl;
								j++;
								break;
								}

							if (c.Low <= tpPrice)
								{
								exitPrice = tpPrice;
								exitType = ExitType.Tp;
								j++;
								break;
								}
							}

						// Если ни TP, ни SL не сработали — держим последнюю цену закрытия.
						exitPrice = c.Close;
						}

					// Если вышли по времени, exitPrice уже равен последнему close < exitUtc.

					double pnl;
					if (direction == TradeDirection.Long)
						pnl = (exitPrice - entryPrice) * qty;
					else
						pnl = (entryPrice - exitPrice) * qty;

					result.TradesTotal++;
					if (pnl > 0.0)
						{
						result.TradesProfitable++;
						result.GrossProfitUsd += pnl;
						withdrawn += pnl;
						}
					else if (pnl < 0.0)
						{
						result.TradesLossy++;
						result.GrossLossUsd += pnl;
						balance += pnl;

						if (balance < balanceMin)
							balanceMin = balance;
						}

					if (exitType == ExitType.Tp) result.ExitTpCount++;
					else if (exitType == ExitType.Sl) result.ExitSlCount++;
					else result.ExitTimeCount++;

					dayPnl += pnl;
					dayTrades++;

					// Распределение по часу входа (локальное время NY).
					var entryLocal = TimeZoneInfo.ConvertTimeFromUtc (entryCandle.OpenTimeUtc, TimeZones.NewYork);
					int hour = entryLocal.Hour;

					if (!hourBuckets.TryGetValue (hour, out var hourBucket))
						{
						hourBucket = new EntryHourBucketStats { HourLocal = hour };
						hourBuckets[hour] = hourBucket;
						}

					hourBucket.Trades++;
					hourBucket.PnlUsd += pnl;

					// Динамика стейка.
					double currentStakeUsd = balance * StakeFraction;
					if (currentStakeUsd < stakeMinUsd)
						stakeMinUsd = currentStakeUsd;

					// Дроудаун по балансу от исходного StartBalanceUsd.
					double drawdownUsd = StartBalanceUsd - balance;
					if (drawdownUsd > result.MaxDrawdownUsd)
						result.MaxDrawdownUsd = drawdownUsd;

					currentIndex = j;
					if (currentIndex >= orderedCandles.Count)
						break;
					if (orderedCandles[currentIndex].OpenTimeUtc >= exitUtc)
						break;
					}

				if (dayTrades > 0)
					{
					var dayStat = new StrategyDayStats
						{
						DateUtc = day.Date,
						DayPnlUsd = dayPnl,
						Trades = dayTrades,
						AtrPct = day.AtrPct
						};

					dayStatsList.Add (dayStat);

					// Распределение по дням недели.
					var dow = day.Date.DayOfWeek;
					if (!weekdayBuckets.TryGetValue (dow, out var dayBucket))
						{
						dayBucket = new WeekdayBucketStats { DayOfWeek = dow };
						weekdayBuckets[dow] = dayBucket;
						}

					dayBucket.Days++;
					dayBucket.Trades += dayTrades;
					dayBucket.PnlUsd += dayPnl;

					// Серии убыточных дней.
					if (dayPnl < 0.0)
						{
						losingStreakCurrent++;
						if (losingStreakCurrent > losingStreakMax)
							losingStreakMax = losingStreakCurrent;
						}
					else if (dayPnl > 0.0)
						{
						losingStreakCurrent = 0;
						}

					if (dayTrades > maxTradesInSingleDay)
						maxTradesInSingleDay = dayTrades;

					var equityAfterDay = balance + withdrawn;
					equityCurve.Add (new EquityPoint
						{
						DateUtc = day.Date,
						EquityUsd = equityAfterDay
						});
					}
				}

			// Заполняем агрегаты по капиталу и риску.
			result.StartBalanceUsd = StartBalanceUsd;
			result.EndBalanceUsd = balance;
			result.WithdrawnProfitUsd = withdrawn;

			result.MaxDrawdownPct = StartBalanceUsd > 0.0
				? result.MaxDrawdownUsd / StartBalanceUsd * 100.0
				: 0.0;

			result.StakeStartUsd = stakeStartUsd;
			result.StakeMinUsd = stakeMinUsd;
			result.StakeMinDrawdownPct = stakeStartUsd > 0.0
				? (stakeStartUsd - stakeMinUsd) / stakeStartUsd * 100.0
				: 0.0;

			// По дням.
			result.DaysTotal = dayStatsList.Count;
			if (result.DaysTotal > 0 && result.TradesTotal > 0)
				{
				result.AvgTradesPerDay = (double) result.TradesTotal / result.DaysTotal;
				}

			result.MaxTradesInSingleDay = maxTradesInSingleDay;
			result.MaxLosingStreakDays = losingStreakMax;

			// Tail-метрики и ATR-квантили.
			FillTailAndVolatilityBuckets (result);

			return result;
			}

		private static int FindFirstCandleIndex ( List<Candle1m> candles, DateTime timeUtc )
			{
			int lo = 0;
			int hi = candles.Count - 1;
			int best = -1;

			while (lo <= hi)
				{
				int mid = lo + (hi - lo) / 2;
				if (candles[mid].OpenTimeUtc >= timeUtc)
					{
					best = mid;
					hi = mid - 1;
					}
				else
					{
					lo = mid + 1;
					}
				}

			return best;
			}

		private static void FillTailAndVolatilityBuckets ( MultiRoundStrategyResult result )
			{
			var days = result.DayStats;
			if (days.Count == 0)
				return;

			// 5 % худших и лучших дней.
			var orderedByPnl = days
				.OrderBy (d => d.DayPnlUsd)
				.ToList ();

			int tailCount = (int) Math.Floor (orderedByPnl.Count * 0.05);
			if (tailCount <= 0)
				tailCount = 1;
			if (tailCount > orderedByPnl.Count)
				tailCount = orderedByPnl.Count;

			for (int i = 0; i < tailCount; i++)
				{
				result.WorstDays.Add (orderedByPnl[i]);
				}

			for (int i = orderedByPnl.Count - tailCount; i < orderedByPnl.Count; i++)
				{
				result.BestDays.Add (orderedByPnl[i]);
				}

			// ATR-квантили (по дневному AtrPct).
			var atrSorted = days
				.Select (d => d.AtrPct)
				.OrderBy (x => x)
				.ToList ();

			if (atrSorted.Count == 0)
				return;

			double q25 = Quantile (atrSorted, 0.25);
			double q50 = Quantile (atrSorted, 0.50);
			double q75 = Quantile (atrSorted, 0.75);

			var b1 = new VolatilityBucketStats { Name = "Q1 (low)" };
			var b2 = new VolatilityBucketStats { Name = "Q2 (mid-low)" };
			var b3 = new VolatilityBucketStats { Name = "Q3 (mid-high)" };
			var b4 = new VolatilityBucketStats { Name = "Q4 (high)" };

			foreach (var day in days)
				{
				double atr = day.AtrPct;
				VolatilityBucketStats bucket;

				if (atr <= q25)
					bucket = b1;
				else if (atr <= q50)
					bucket = b2;
				else if (atr <= q75)
					bucket = b3;
				else
					bucket = b4;

				bucket.Days++;
				bucket.Trades += day.Trades;
				bucket.PnlUsd += day.DayPnlUsd;
				}

			double atrMin = atrSorted.First ();
			double atrMax = atrSorted.Last ();

			b1.AtrFrom = atrMin;
			b1.AtrTo = q25;

			b2.AtrFrom = q25;
			b2.AtrTo = q50;

			b3.AtrFrom = q50;
			b3.AtrTo = q75;

			b4.AtrFrom = q75;
			b4.AtrTo = atrMax;

			result.PnlByAtrBucket.Add (b1);
			result.PnlByAtrBucket.Add (b2);
			result.PnlByAtrBucket.Add (b3);
			result.PnlByAtrBucket.Add (b4);
			}

		private static double Quantile ( List<double> sorted, double p )
			{
			if (sorted == null) throw new ArgumentNullException (nameof (sorted));
			if (sorted.Count == 0) return 0.0;

			if (p <= 0.0) return sorted[0];
			if (p >= 1.0) return sorted[sorted.Count - 1];

			double index = (sorted.Count - 1) * p;
			int lo = (int) Math.Floor (index);
			int hi = (int) Math.Ceiling (index);

			if (lo == hi)
				return sorted[lo];

			double weight = index - lo;
			return sorted[lo] + (sorted[hi] - sorted[lo]) * weight;
			}
		}
	}
