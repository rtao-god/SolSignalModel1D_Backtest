using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// PnL-движок: дневной вход (TP/SL/close дня), delayed A/B, ликвидации по 1m,
	/// Cross/Isolated, бакеты капитала и вывод сверхбазы (withdrawals).
	/// Поддерживает режимы with/without SL (для daily и delayed).
	/// </summary>
	public static class PnlCalculator
		{
		// === Константы комиссии/капитала ===
		private const double CommissionRate = 0.0004;     // ~Binance Taker 4 б.п. на вход/выход
		private const double TotalCapital = 20000.0;

		// === Распределение капитала по бакетам ===
		private const double DailyShare = 0.60;
		private const double IntradayShare = 0.25;       // бакет зарезервирован, сейчас не торгуем
		private const double DelayedShare = 0.15;

		// === Размер позиции внутри бакета (доля бакета) ===
		private const double DailyPositionFraction = 1.0;
		private const double IntradayPositionFraction = 0.0; // интрадей пока отключен
		private const double DelayedPositionFraction = 0.4;

		// === Биржевая математика ликвидации (упрощенно) ===
		private const double MaintenanceMarginRate = 0.004;

		private sealed class BucketState
			{
			public string Name = string.Empty;
			public double BaseCapital;
			public double Equity;
			public double PeakVisible;
			public double MaxDd;
			public double Withdrawn;
			public bool IsDead;
			}

		// ---------------------------------------------------------------------
		// ПОДДЕРЖКА СТАРОГО ВЫЗОВА (как в твоем текущем коде)
		// ---------------------------------------------------------------------
		/// <summary>
		/// Старый вариант сигнатуры (сохраняю для совместимости с текущими вызовами).
		/// useStopLoss → управляет SL и в daily, и в delayed.
		/// dailyStopPct → дневной SL (% от цены входа).
		/// TP фиксирован 3% (как у тебя было).
		/// </summary>
		public static void ComputePnL (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			ILeveragePolicy policy,
			MarginMode marginMode,
			bool useStopLoss,
			double dailyStopPct,
			out List<PnLTrade> trades,
			out double totalPnlPct,
			out double maxDdPct,
			out Dictionary<string, int> tradesBySource,
			out double withdrawnTotal,
			out List<PnlBucketSnapshot> bucketSnapshots,
			out bool hadLiquidation )
			{
			ComputePnL (
				records, candles1m, policy, marginMode,
				out trades, out totalPnlPct, out maxDdPct, out tradesBySource,
				out withdrawnTotal, out bucketSnapshots, out hadLiquidation,
				useDailyStopLoss: useStopLoss,
				useDelayedIntradayStops: useStopLoss,
				dailyTpPct: 0.03,
				dailyStopPct: dailyStopPct <= 0 ? 0.05 : dailyStopPct
			);
			}

		// ---------------------------------------------------------------------
		// ОСНОВНОЙ СОВРЕМЕННЫЙ ВАРИАНТ
		// ---------------------------------------------------------------------
		/// <summary>
		/// Главный расчёт PnL.
		/// - useDailyStopLoss: управляет дневным SL (без него — только TP или close дня).
		/// - useDelayedIntradayStops: управляет respect к intraday SL в delayed-модели (SlFirst).
		/// - dailyTpPct: дневной TP (по умолчанию 3%).
		/// - dailyStopPct: дневной SL (по умолчанию 5%).
		/// </summary>
		public static void ComputePnL (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			ILeveragePolicy policy,
			MarginMode marginMode,
			out List<PnLTrade> trades,
			out double totalPnlPct,
			out double maxDdPct,
			out Dictionary<string, int> tradesBySource,
			out double withdrawnTotal,
			out List<PnlBucketSnapshot> bucketSnapshots,
			out bool hadLiquidation,
			bool useDailyStopLoss = true,
			bool useDelayedIntradayStops = true,
			double dailyTpPct = 0.03,
			double dailyStopPct = 0.05 )
			{
			if (candles1m == null || candles1m.Count == 0)
				throw new InvalidOperationException ("[pnl] 1m candles are required for liquidation/TP/SL.");

			// Сортированные минутки
			var m1 = candles1m.OrderBy (m => m.OpenTimeUtc).ToList ();

			// Журнал результатов
			var resultTrades = new List<PnLTrade> ();
			var resultBySource = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
			var buckets = InitBuckets ();

			double withdrawnLocal = 0.0;
			bool anyLiquidation = false;
			bool globalDead = false;

			// ЛОКАЛЬНАЯ ФУНКЦИЯ: регистрация трейда + обновление бакета/метрик
			void RegisterTrade (
				DateTime dayUtc,
				DateTime entryTimeUtc,
				DateTime exitTimeUtc,
				string source,
				string bucketName,
				double positionFraction,
				bool isLong,
				double entryPrice,
				double exitPrice,
				double leverage,
				List<Candle1m> tradeMinutes )
				{
				if (globalDead) return;
				if (!buckets.TryGetValue (bucketName, out var bucket)) return;
				if (bucket.IsDead) return;
				if (leverage <= 0.0 || positionFraction <= 0.0) return;

				double posBase = bucket.BaseCapital * positionFraction;
				if (posBase <= 0.0) return;

				// 1) Ликвидация через минутки
				var (liqHit, liqExit) = CheckLiquidation (entryPrice, isLong, leverage, tradeMinutes);
				bool priceLiquidated = liqHit;
				double finalExitPrice = liqHit ? liqExit : exitPrice;

				// Безопасность: если переданный exit хуже ликвида — затыкаем ликвид
				finalExitPrice = CapWorseThanLiquidation (entryPrice, isLong, leverage, finalExitPrice, out bool forceLiq);
				if (forceLiq) priceLiquidated = true;

				// 2) Доходность/комиссия
				double relMove = isLong ? (finalExitPrice - entryPrice) / entryPrice
											 : (entryPrice - finalExitPrice) / entryPrice;
				double notional = posBase * leverage;
				double positionPnl = relMove * leverage * posBase;
				double positionComm = notional * CommissionRate * 2.0;

				// 3) Обновляем equity бакета и global state
				UpdateBucketEquity (
					marginMode, bucket, posBase, positionPnl, positionComm,
					priceLiquidated, ref withdrawnLocal, out bool diedThisTrade);

				if (diedThisTrade)
					{
					anyLiquidation = true;
					globalDead = true;
					}

				// 4) Пишем в лог сделок
				resultTrades.Add (new PnLTrade
					{
					DateUtc = dayUtc,
					EntryTimeUtc = entryTimeUtc,
					ExitTimeUtc = exitTimeUtc,
					Source = source,
					Bucket = bucketName,
					IsLong = isLong,
					EntryPrice = entryPrice,
					ExitPrice = finalExitPrice,
					PositionUsd = posBase,
					GrossReturnPct = Math.Round (relMove * 100.0, 4),
					NetReturnPct = Math.Round ((positionPnl - positionComm) / posBase * 100.0, 4),
					Commission = Math.Round (positionComm, 4),
					EquityAfter = Math.Round (bucket.Equity, 2),
					IsLiquidated = priceLiquidated || (marginMode == MarginMode.Isolated && bucket.Equity <= 0.0),
					LeverageUsed = leverage
					});

				if (!resultBySource.TryGetValue (source, out var cnt))
					resultBySource[source] = 1;
				else
					resultBySource[source] = cnt + 1;
				}

			// ===== Основной цикл по дням =====
			foreach (var rec in records.OrderBy (r => r.DateUtc))
				{
				if (globalDead) break;

				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort) continue;

				double lev = policy.ResolveLeverage (rec);
				if (lev <= 0.0) continue;

				DateTime dayStart = rec.DateUtc;
				DateTime dayEnd = rec.DateUtc.AddHours (24);

				var dayMinutes = SliceDayMinutes (m1, dayStart, dayEnd);
				if (dayMinutes.Count == 0)
					throw new InvalidOperationException ($"[pnl] 1m candles missing for {rec.DateUtc:yyyy-MM-dd}");

				double entry = rec.Entry;

				// ===== DAILY (TP/SL/Close) =====
					{
					double slPct = useDailyStopLoss ? dailyStopPct : 0.0;
					var (exitPrice, exitTimeUtc) = TryHitDailyExit (entry, goLong, dailyTpPct, slPct, dayMinutes);

					RegisterTrade (
						rec.DateUtc, rec.DateUtc, exitTimeUtc,
						"Daily", "daily", DailyPositionFraction,
						isLong: goLong, entryPrice: entry, exitPrice: exitPrice,
						leverage: lev, tradeMinutes: dayMinutes);
					}

				if (globalDead) break;

				// ===== DELAYED (интрадей TP/SL от delayed-логики ИЛИ close дня) =====
				if (!string.IsNullOrEmpty (rec.DelayedSource) && rec.DelayedEntryExecuted)
					{
					bool dLong = goLong;
					double dEntry = rec.DelayedEntryPrice;
					DateTime delayedEntryTime = rec.DelayedEntryExecutedAtUtc ?? rec.DateUtc;

					var delayedMinutes = dayMinutes.Where (m => m.OpenTimeUtc >= delayedEntryTime).ToList ();
					if (delayedMinutes.Count == 0)
						throw new InvalidOperationException ($"[pnl] delayed 1m candles missing for {rec.DateUtc:yyyy-MM-dd}");

					double dExit;
					DateTime delayedExitTime;

					if (rec.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst)
						{
						double tpPctD = rec.DelayedIntradayTpPct;
						dExit = dLong ? dEntry * (1.0 + tpPctD) : dEntry * (1.0 - tpPctD);
						delayedExitTime = delayedMinutes.First ().OpenTimeUtc;
						}
					else if (rec.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst && useDelayedIntradayStops)
						{
						double slPctD = rec.DelayedIntradaySlPct;
						dExit = dLong ? dEntry * (1.0 - slPctD) : dEntry * (1.0 + slPctD);
						delayedExitTime = delayedMinutes.First ().OpenTimeUtc;
						}
					else
						{
						// без SL или ни TP/SL — по close дня
						dExit = delayedMinutes.Last ().Close;
						delayedExitTime = delayedMinutes.Last ().OpenTimeUtc;
						}

					string src = rec.DelayedSource == "A" ? "DelayedA" : "DelayedB";

					RegisterTrade (
						rec.DateUtc,
						delayedEntryTime,
						delayedExitTime,
						src,
						"delayed",
						DelayedPositionFraction,
						dLong,
						dEntry,
						dExit,
						lev,
						delayedMinutes
					);
					}

				if (globalDead) break;
				}

			// ===== Финализация метрик =====
			double finalEquity = buckets.Values.Sum (b => b.Equity);
			double finalWithdrawn = buckets.Values.Sum (b => b.Withdrawn);
			double finalTotal = finalEquity + finalWithdrawn;

			double maxDdAll = buckets.Values.Count > 0
				? buckets.Values.Max (b => b.MaxDd)
				: 0.0;

			trades = resultTrades;
			tradesBySource = resultBySource;
			withdrawnTotal = finalWithdrawn;
			bucketSnapshots = buckets.Values.Select (b => new PnlBucketSnapshot
				{
				Name = b.Name,
				StartCapital = b.BaseCapital,
				EquityNow = b.Equity,
				Withdrawn = b.Withdrawn
				}).ToList ();

			totalPnlPct = Math.Round ((finalTotal - TotalCapital) / TotalCapital * 100.0, 2);
			maxDdPct = Math.Round (maxDdAll * 100.0, 2);
			hadLiquidation = anyLiquidation;
			}

		// =====================================================================
		// Декомпозированные приватные помощники
		// =====================================================================

		private static Dictionary<string, BucketState> InitBuckets ()
			=> new (StringComparer.OrdinalIgnoreCase)
				{
				["daily"] = MakeBucket ("daily", TotalCapital * DailyShare),
				["intraday"] = MakeBucket ("intraday", TotalCapital * IntradayShare),
				["delayed"] = MakeBucket ("delayed", TotalCapital * DelayedShare),
				};

		private static BucketState MakeBucket ( string name, double baseCapital ) => new ()
			{
			Name = name,
			BaseCapital = baseCapital,
			Equity = baseCapital,
			PeakVisible = baseCapital,
			MaxDd = 0.0,
			Withdrawn = 0.0,
			IsDead = false
			};

		private static List<Candle1m> SliceDayMinutes ( List<Candle1m> m1, DateTime start, DateTime end )
			=> m1.Where (m => m.OpenTimeUtc >= start && m.OpenTimeUtc < end).ToList ();

		private static (double exitPrice, DateTime exitTime) TryHitDailyExit (
			double entry, bool isLong, double tpPct, double slPct, List<Candle1m> dayMinutes )
			{
			if (isLong)
				{
				double tp = entry * (1.0 + tpPct);
				double sl = slPct > 1e-9 ? entry * (1.0 - slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.High >= tp;
					bool hitSl = !double.IsNaN (sl) && m.Low <= sl;
					if (hitTp || hitSl)
						{
						return (hitSl ? sl : tp, m.OpenTimeUtc);
						}
					}
				}
			else
				{
				double tp = entry * (1.0 - tpPct);
				double sl = slPct > 1e-9 ? entry * (1.0 + slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.Low <= tp;
					bool hitSl = !double.IsNaN (sl) && m.High >= sl;
					if (hitTp || hitSl)
						{
						return (hitSl ? sl : tp, m.OpenTimeUtc);
						}
					}
				}

			// Ни TP, ни SL — закрываемся по close дня
			var last = dayMinutes.Last ();
			return (last.Close, last.OpenTimeUtc);
			}

		private static (bool hit, double liqExit) CheckLiquidation (
			double entry, bool isLong, double leverage, List<Candle1m> minutes )
			{
			double liqAdversePct = 1.0 / leverage - MaintenanceMarginRate;
			if (liqAdversePct <= 0.0) liqAdversePct = 1.0 / leverage * 0.9;

			if (isLong)
				{
				double liqPrice = entry * (1.0 - liqAdversePct);
				foreach (var m in minutes)
					if (m.Low <= liqPrice)
						return (true, liqPrice);
				return (false, 0.0);
				}
			else
				{
				double liqPrice = entry * (1.0 + liqAdversePct);
				foreach (var m in minutes)
					if (m.High >= liqPrice)
						return (true, liqPrice);
				return (false, 0.0);
				}
			}

		private static double CapWorseThanLiquidation (
			double entry, bool isLong, double leverage, double candidateExit, out bool cappedToLiq )
			{
			double liqAdversePct = 1.0 / leverage - MaintenanceMarginRate;
			if (liqAdversePct <= 0.0) liqAdversePct = 1.0 / leverage * 0.9;

			// Фактическое "насколько в минус" (adverse) у предлагаемого выхода
			double adverseFact = isLong
				? (entry - candidateExit) / entry
				: (candidateExit - entry) / entry;

			if (adverseFact >= liqAdversePct + 1e-7)
				{
				cappedToLiq = true;
				return isLong
					? entry * (1.0 - liqAdversePct)
					: entry * (1.0 + liqAdversePct);
				}

			cappedToLiq = false;
			return candidateExit;
			}

		private static void UpdateBucketEquity (
			MarginMode marginMode,
			BucketState bucket,
			double posBase,
			double positionPnl,
			double positionComm,
			bool priceLiquidated,
			ref double withdrawnLocal,
			out bool died )
			{
			died = false;
			double newEquity = bucket.Equity;

			if (marginMode == MarginMode.Cross)
				{
				if (priceLiquidated)
					{
					newEquity = 0.0;
					bucket.IsDead = true;
					died = true;
					}
				else
					{
					newEquity = bucket.Equity + positionPnl - positionComm;
					if (newEquity < 0) newEquity = 0.0;
					}

				// Выводим всё сверх базовой (не накапливаем капитал в бакете)
				if (!died && newEquity > bucket.BaseCapital)
					{
					double extra = newEquity - bucket.BaseCapital;
					if (extra > 0)
						{
						bucket.Withdrawn += extra;
						withdrawnLocal += extra;
						}
					newEquity = bucket.BaseCapital;
					}
				}
			else // Isolated
				{
				if (priceLiquidated)
					{
					newEquity = bucket.Equity - posBase - positionComm;
					if (newEquity < 0) newEquity = 0.0;
					bucket.IsDead = true;
					died = true;
					}
				else
					{
					newEquity = bucket.Equity + positionPnl - positionComm;
					if (newEquity <= 0.0)
						{
						newEquity = 0.0;
						bucket.IsDead = true;
						died = true;
						}
					else if (newEquity > bucket.BaseCapital)
						{
						double extra = newEquity - bucket.BaseCapital;
						bucket.Withdrawn += extra;
						withdrawnLocal += extra;
						newEquity = bucket.BaseCapital;
						}
					}
				}

			bucket.Equity = newEquity;

			// Пик «видимой» equity = equity + withdrawals
			double visible = bucket.Equity + bucket.Withdrawn;
			if (visible > bucket.PeakVisible)
				bucket.PeakVisible = visible;

			// Макс. просадка по «видимой»
			if (bucket.PeakVisible > 1e-9)
				{
				double dd = (bucket.PeakVisible - visible) / bucket.PeakVisible;
				if (dd > bucket.MaxDd) bucket.MaxDd = dd;
				}
			}
		}
	}
