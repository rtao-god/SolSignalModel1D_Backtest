using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Omniscient.Trading;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Utils.Backtest
	{
	/// <summary>
	/// Вспомогательные расчёты для таблиц WITH/WITHOUT SL:
	/// - wealth-база (Total %, Total $, Withdrawn, OnExch);
	/// - Long/Short $PnL, усечённые под Total $;
	/// - балансные и риск-метрики по active equity;
	/// - средние дневные/недельные/месячные/годовые доходности по календарным дням.
	/// </summary>
	internal static class PolicySlMetrics
		{
		/// <summary>
		/// Порог «балансовой смерти»: equity падала ниже 35% от стартового капитала.
		/// Используется и в расчётах, и в раскраске строк.
		/// </summary>
		internal const double BalanceDeathThresholdFrac = 0.35;

		/// <summary>
		/// Готовый набор метрик по одной строке таблицы (Policy + Margin + Mode).
		/// Используется для печати всех трёх таблиц.
		/// </summary>
		internal sealed class PolicyRowMetrics
			{
			// Идентификация строки
			public string PolicyName { get; init; } = string.Empty;
			public MarginMode Margin { get; init; }
			public string Mode { get; init; } = string.Empty;

			// Базовые PnL-метрики (wealth-база)
			public int TradesCount { get; init; }
			public double TotalPct { get; init; }
			public double TotalUsd { get; init; }
			public double WithdrawnUsd { get; init; }
			public double OnExchUsd { get; init; }   // equityNow

			// MaxDD по active equity (фракция 0..1)
			public double MaxDdFrac { get; init; }

			// Декомпозиция по long/short
			public int LongCount { get; init; }
			public int ShortCount { get; init; }
			public double LongUsd { get; init; }
			public double ShortUsd { get; init; }
			public double AvgLongPct { get; init; }
			public double AvgShortPct { get; init; }

			// Ликвидации / руин-метрики

			/// <summary>
			/// Ликвидации счёта / аккаунта в рамках политики.
			/// Для cross: фактически «смерть» всего аккаунта после первой критической просадки.
			/// Для isolated: достаточно смерти хотя бы одного бакета.
			/// </summary>
			public int AccountRuinCount { get; init; }

			/// <summary>
			/// Количество сделок, где в бэктесте сработала «реальная» ликвидация позиции
			/// (флаг <see cref="PnLTrade.IsRealLiquidation"/>).
			/// Для cross значение хранится для полноты, но в таблицах выводится «—»,
			/// т.к. там важнее сам факт окончательной смерти, а не число таких случаев.
			/// </summary>
			public int RealLiqCount { get; init; }

			// Балансные метрики
			public double StartCapital { get; init; }
			public double BalMinFrac { get; init; }   // minEquity / StartCapital
			public bool BalDead { get; init; }

			public bool Recovered { get; init; }
			public double RecovDaysCal { get; init; }
			public int RecovSignals { get; init; }
			public double TimeBelowThreshDays { get; init; }  // суммарно дней ниже порога (35%)
			public double ReqGainPct { get; init; }            // ReqGain% @Min

			// Горизонт и средние доходности
			public double HorizonDays { get; init; }    // длительность теста в календарных днях
			public double AvgDailyPct { get; init; }    // средняя дневная доходность (%)
			public double AvgWeeklyPct { get; init; }   // средняя недельная (%)
			public double AvgMonthlyPct { get; init; }  // средняя месячная (%)
			public double AvgYearlyPct { get; init; }   // средняя годовая (%)
			}

		/// <summary>
		/// Построение полного набора метрик по одной политике
		/// (в режиме "with SL" или "no SL").
		/// </summary>
		internal static PolicyRowMetrics BuildMetrics ( BacktestPolicyResult r, string mode )
			{
			var trades = r.Trades ?? new List<PnLTrade> ();
			var longs = trades.Where (x => x.IsLong).ToList ();
			var shorts = trades.Where (x => !x.IsLong).ToList ();

			// --- Wealth-база: StartCapital, OnExch (equityNow), Withdrawn ---

			double startCapital = 0.0;
			double equityNow = 0.0;

			if (r.BucketSnapshots != null && r.BucketSnapshots.Count > 0)
				{
				startCapital = r.BucketSnapshots.Sum (b => b.StartCapital);
				equityNow = r.BucketSnapshots.Sum (b => b.EquityNow);
				}

			double withdrawn = r.WithdrawnTotal;
			double wealthNow = equityNow + withdrawn;

			double totalUsd;
			double totalPct;

			if (startCapital > 0.0)
				{
				totalUsd = wealthNow - startCapital;
				totalPct = (wealthNow - startCapital) / startCapital * 100.0;
				}
			else
				{
				// Без стартового капитала невозможно честно посчитать $PnL,
				// оставляем только TotalPnlPct из результата бэктеста.
				totalPct = r.TotalPnlPct;
				totalUsd = 0.0;
				}

			double onExchUsd = equityNow;

			// --- "сырые" long/short PnL из сделок ---

			double longUsdRaw = longs.Sum (x => x.PositionUsd * (x.NetReturnPct / 100.0));
			double shortUsdRaw = shorts.Sum (x => x.PositionUsd * (x.NetReturnPct / 100.0));
			double rawTotal = longUsdRaw + shortUsdRaw;

			double longUsd = longUsdRaw;
			double shortUsd = shortUsdRaw;

			// Подгоняем Long$/Short$ под wealth-based Total$ (чтобы суммы сходились)
			if (startCapital > 0.0 && Math.Abs (rawTotal) > 1e-9)
				{
				double scale = totalUsd / rawTotal;
				longUsd = longUsdRaw * scale;
				shortUsd = shortUsdRaw * scale;
				}
			else if (startCapital > 0.0 && Math.Abs (rawTotal) <= 1e-9)
				{
				// Если по сделкам почти ноль, а Total$ ≠ 0 — показываем только Total$.
				longUsd = 0.0;
				shortUsd = 0.0;
				}

			double avgLongPct = longs.Count > 0 ? longs.Average (x => x.NetReturnPct) : 0.0;
			double avgShortPct = shorts.Count > 0 ? shorts.Average (x => x.NetReturnPct) : 0.0;

			// --- Ликвидации/руин ---

			// Было ли хоть одно «руин-событие» на уровне политики (смерть бакета).
			int accountRuinCount = r.HadLiquidation ? 1 : 0;

			// Реальные ликвидации считаем только для isolated.
			// Критерий — флаг IsRealLiquidation на сделке.
			int realLiqCount = 0;
			if (r.Margin == MarginMode.Isolated)
				{
				realLiqCount = trades.Count (x => x.IsRealLiquidation);
				}

			// --- Балансные метрики по active equity ---
			var bal = ComputeBalanceStats (r);

			// --- Горизонт и средние доходности по календарным дням ---

			double horizonDays = 0.0;
			double avgDailyPct = 0.0;
			double avgWeeklyPct = 0.0;
			double avgMonthlyPct = 0.0;
			double avgYearlyPct = 0.0;

			if (startCapital > 0.0 && trades.Count > 0)
				{
				var firstExit = trades.Min (t => t.ExitTimeUtc);
				var lastExit = trades.Max (t => t.ExitTimeUtc);
				horizonDays = Math.Max (1.0, (lastExit - firstExit).TotalDays);

				if (horizonDays > 0.0)
					{
					double totalMult = wealthNow / startCapital;
					if (totalMult > 0.0)
						{
						double dailyMult = Math.Pow (totalMult, 1.0 / horizonDays);
						avgDailyPct = (dailyMult - 1.0) * 100.0;

						double weekMult = Math.Pow (dailyMult, 7.0);
						double monthMult = Math.Pow (dailyMult, 30.0);
						double yearMult = Math.Pow (dailyMult, 365.0);

						avgWeeklyPct = (weekMult - 1.0) * 100.0;
						avgMonthlyPct = (monthMult - 1.0) * 100.0;
						avgYearlyPct = (yearMult - 1.0) * 100.0;
						}
					}
				}

			return new PolicyRowMetrics
				{
				PolicyName = r.PolicyName,
				Margin = r.Margin,
				Mode = mode,

				TradesCount = trades.Count,
				TotalPct = totalPct,
				TotalUsd = totalUsd,
				WithdrawnUsd = withdrawn,
				OnExchUsd = onExchUsd,
				MaxDdFrac = bal.MaxDdFrac,

				LongCount = longs.Count,
				ShortCount = shorts.Count,
				LongUsd = longUsd,
				ShortUsd = shortUsd,
				AvgLongPct = avgLongPct,
				AvgShortPct = avgShortPct,

				AccountRuinCount = accountRuinCount,
				RealLiqCount = realLiqCount,

				StartCapital = bal.StartCapital,
				BalMinFrac = bal.MinEquityFrac,
				BalDead = bal.IsBalanceDead,

				Recovered = bal.Recovered,
				RecovDaysCal = bal.RecovDaysCal,
				RecovSignals = bal.RecovSignals,
				TimeBelowThreshDays = bal.TimeBelowThreshDays,
				ReqGainPct = bal.ReqGainPct,

				HorizonDays = horizonDays,
				AvgDailyPct = avgDailyPct,
				AvgWeeklyPct = avgWeeklyPct,
				AvgMonthlyPct = avgMonthlyPct,
				AvgYearlyPct = avgYearlyPct
				};
			}

		/// <summary>
		/// Внутренние балансные метрики по active equity.
		/// </summary>
		private sealed class BalanceStats
			{
			public double StartCapital { get; init; }
			public double MinEquityFrac { get; init; }
			public bool IsBalanceDead { get; init; }

			public double MaxDdFrac { get; init; }
			public bool Recovered { get; init; }
			public double RecovDaysCal { get; init; }
			public int RecovSignals { get; init; }

			public double TimeBelowThreshDays { get; init; }
			public double ReqGainPct { get; init; }
			}

		/// <summary>
		/// Полный расчёт кривой active equity и связанных метрик:
		/// MinEquity, MaxDD, восстановление, время ниже порога (35%), ReqGain% и т.д.
		/// </summary>
		private static BalanceStats ComputeBalanceStats ( BacktestPolicyResult r )
			{
			var snaps = r.BucketSnapshots ?? new List<PnlBucketSnapshot> ();
			var trades = r.Trades ?? new List<PnLTrade> ();

			if (snaps.Count == 0 || trades.Count == 0)
				{
				return new BalanceStats
					{
					StartCapital = snaps.Sum (s => s.StartCapital),
					MinEquityFrac = 1.0,
					IsBalanceDead = false,
					MaxDdFrac = 0.0,
					Recovered = false,
					RecovDaysCal = 0.0,
					RecovSignals = 0,
					TimeBelowThreshDays = 0.0,
					ReqGainPct = 0.0
					};
				}

			double startCapital = snaps.Sum (s => s.StartCapital);
			if (startCapital <= 0.0)
				{
				return new BalanceStats
					{
					StartCapital = 0.0,
					MinEquityFrac = 1.0,
					IsBalanceDead = false,
					MaxDdFrac = 0.0,
					Recovered = false,
					RecovDaysCal = 0.0,
					RecovSignals = 0,
					TimeBelowThreshDays = 0.0,
					ReqGainPct = 0.0
					};
				}

			// --- Кривая active equity по ExitTimeUtc ---

			var bucketEquity = snaps.ToDictionary (
				s => s.Name,
				s => s.StartCapital,
				StringComparer.OrdinalIgnoreCase);

			var orderedTrades = trades
				.OrderBy (x => x.ExitTimeUtc)
				.ToList ();

			if (orderedTrades.Count == 0)
				{
				return new BalanceStats
					{
					StartCapital = startCapital,
					MinEquityFrac = 1.0,
					IsBalanceDead = false,
					MaxDdFrac = 0.0,
					Recovered = false,
					RecovDaysCal = 0.0,
					RecovSignals = 0,
					TimeBelowThreshDays = 0.0,
					ReqGainPct = 0.0
					};
				}

			var curve = new List<(DateTime Time, double Equity)> ();

			DateTime firstTime = orderedTrades.First ().ExitTimeUtc;
			curve.Add ((firstTime, startCapital));

			foreach (var t in orderedTrades)
				{
				if (!bucketEquity.ContainsKey (t.Bucket))
					{
					// Новый бакет — инициализируем его EquityAfter.
					bucketEquity[t.Bucket] = t.EquityAfter;
					}
				else
					{
					bucketEquity[t.Bucket] = t.EquityAfter;
					}

				double activeTotal = bucketEquity.Values.Sum ();
				curve.Add ((t.ExitTimeUtc, activeTotal));
				}

			if (curve.Count == 0)
				{
				return new BalanceStats
					{
					StartCapital = startCapital,
					MinEquityFrac = 1.0,
					IsBalanceDead = false,
					MaxDdFrac = 0.0,
					Recovered = false,
					RecovDaysCal = 0.0,
					RecovSignals = 0,
					TimeBelowThreshDays = 0.0,
					ReqGainPct = 0.0
					};
				}

			// --- Основные величины по кривой ---

			double minEquity = startCapital;
			DateTime minEquityTime = curve[0].Time;

			double peakEquity = startCapital;
			DateTime peakEquityTime = curve[0].Time;

			double maxDdFrac = 0.0;
			DateTime ddValleyTime = curve[0].Time;
			DateTime ddPeakTime = curve[0].Time;

			foreach (var (time, value) in curve)
				{
				// глобальный минимум equity
				if (value < minEquity)
					{
					minEquity = value;
					minEquityTime = time;
					}

				// локальный пик
				if (value > peakEquity)
					{
					peakEquity = value;
					peakEquityTime = time;
					}

				// просадка от пика
				if (peakEquity > 1e-9)
					{
					double dd = (peakEquity - value) / peakEquity;
					if (dd > maxDdFrac)
						{
						maxDdFrac = dd;
						ddValleyTime = time;
						ddPeakTime = peakEquityTime;
						}
					}
				}

			double minFrac = minEquity / startCapital;
			bool dead = minFrac <= BalanceDeathThresholdFrac;

			// --- Восстановление после MaxDD до уровня пика ---

			bool recovered = false;
			double recovDaysCal = -1.0;
			int recovSignals = 0;

			if (maxDdFrac > 0.0)
				{
				double targetPeak = 0.0;

				foreach (var (time, value) in curve)
					{
					if (time <= ddPeakTime && value > targetPeak)
						targetPeak = value;
					}

				if (targetPeak <= 0.0)
					targetPeak = peakEquity;

				DateTime? recoverTime = null;

				foreach (var (time, value) in curve
					.Where (c => c.Time > ddValleyTime)
					.OrderBy (c => c.Time))
					{
					if (value >= targetPeak)
						{
						recoverTime = time;
						break;
						}
					}

				if (recoverTime.HasValue)
					{
					recovered = true;
					recovDaysCal = (recoverTime.Value - ddValleyTime).TotalDays;

					// Сигнальные дни: количество уникальных trade-дней
					// между дном просадки и моментом восстановления.
					var signalDays = r.Trades?
						.Where (tr => tr.ExitTimeUtc > ddValleyTime && tr.ExitTimeUtc <= recoverTime.Value)
						.Select (tr => tr.DateUtc.ToCausalDateUtc())
						.Distinct ()
						.Count () ?? 0;

					recovSignals = signalDays;
					}
				}

			// --- Время ниже порога (35% от старта) в днях ---

			double thresholdEquity = BalanceDeathThresholdFrac * startCapital;
			double timeBelowDays = 0.0;

			if (curve.Count >= 2)
				{
				for (int i = 0; i < curve.Count - 1; i++)
					{
					var (t0, e0) = curve[i];
					var (t1, e1) = curve[i + 1];

					if (t1 <= t0) continue;
					double dtDays = (t1 - t0).TotalDays;
					if (dtDays <= 0) continue;

					// оба выше порога
					if (e0 >= thresholdEquity && e1 >= thresholdEquity)
						continue;

					// оба ниже порога
					if (e0 < thresholdEquity && e1 < thresholdEquity)
						{
						timeBelowDays += dtDays;
						continue;
						}

					// пересечение порога внутри интервала
					if (Math.Abs (e1 - e0) < 1e-9)
						{
						timeBelowDays += dtDays * 0.5;
						continue;
						}

					double frac = (thresholdEquity - e0) / (e1 - e0);
					if (frac < 0.0) frac = 0.0;
					if (frac > 1.0) frac = 1.0;

					var crossTime = t0 + TimeSpan.FromTicks (
						(long) ((t1 - t0).Ticks * frac));

					if (e0 >= thresholdEquity && e1 < thresholdEquity)
						{
						// сверху вниз
						timeBelowDays += (t1 - crossTime).TotalDays;
						}
					else if (e0 < thresholdEquity && e1 >= thresholdEquity)
						{
						// снизу вверх
						timeBelowDays += (crossTime - t0).TotalDays;
						}
					}
				}

			// --- Требуемый прирост с минимума до пика MaxDD ---

			double reqGainPct = 0.0;
			if (maxDdFrac > 0.0 && maxDdFrac < 0.999999)
				{
				// ReqGain% = (1 / (1 - MaxDD) - 1) * 100
				reqGainPct = (1.0 / (1.0 - maxDdFrac) - 1.0) * 100.0;
				}
			else if (maxDdFrac >= 0.999999)
				{
				reqGainPct = double.PositiveInfinity;
				}

			return new BalanceStats
				{
				StartCapital = startCapital,
				MinEquityFrac = minFrac,
				IsBalanceDead = dead,
				MaxDdFrac = maxDdFrac,
				Recovered = recovered,
				RecovDaysCal = recovDaysCal,
				RecovSignals = recovSignals,
				TimeBelowThreshDays = timeBelowDays,
				ReqGainPct = reqGainPct
				};
			}
		}
	}
