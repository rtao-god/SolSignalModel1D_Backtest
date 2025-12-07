using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// PnL-движок: дневной вход (TP/SL/close), delayed A/B, ликвидации по 1m,
	/// Cross/Isolated, бакеты капитала и вывод сверхбазы (withdrawals).
	/// Поддерживает режимы with/without SL (для daily и delayed).
	/// Разбит на partial-файлы: ядро, бакеты, ликвидация, дневной TP/SL, Anti-D и т.п.
	/// </summary>
	public static partial class PnlCalculator
		{
		// === Константы комиссии/капитала ===
		private const double CommissionRate = 0.0004;     // ~Binance Taker 4 б.п. на вход/выход
		private const double TotalCapital = 20000.0;

		// === Распределение капитала по бакетам ===
		private const double DailyShare = 0.60;
		private const double IntradayShare = 0.25;        // бакет зарезервирован, сейчас не торгуем
		private const double DelayedShare = 0.15;

		// === Размер позиции внутри бакета (доля бакета) ===
		private const double DailyPositionFraction = 1.0;
		private const double IntradayPositionFraction = 0.0; // интрадей пока отключен
		private const double DelayedPositionFraction = 0.4;

		// ---------------------------------------------------------------------
		// ПОДДЕРЖКА СТАРОГО ВЫЗОВА
		// ---------------------------------------------------------------------
		/// <summary>
		/// Старый вариант сигнатуры (для совместимости).
		/// useStopLoss → управляет SL и в daily, и в delayed.
		/// dailyStopPct → дневной SL (% от цены входа).
		/// TP фиксирован 3%.
		/// Доп. флаг useAntiDirectionOverlay → включить Anti-D overlay в PnL.
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
			out bool hadLiquidation,
			bool useAntiDirectionOverlay = false )
			{
			ComputePnL (
				records,
				candles1m,
				policy,
				marginMode,
				out trades,
				out totalPnlPct,
				out maxDdPct,
				out tradesBySource,
				out withdrawnTotal,
				out bucketSnapshots,
				out hadLiquidation,
				useDailyStopLoss: useStopLoss,
				useDelayedIntradayStops: useStopLoss,
				dailyTpPct: 0.03,
				dailyStopPct: dailyStopPct <= 0 ? 0.05 : dailyStopPct,
				useAntiDirectionOverlay: useAntiDirectionOverlay
			);
			}

		// ---------------------------------------------------------------------
		// ОСНОВНОЙ СОВРЕМЕННЫЙ ВАРИАНТ
		// ---------------------------------------------------------------------
		/// <summary>
		/// Главный расчёт PnL.
		/// - useDailyStopLoss: управляет дневным SL (без него — только TP или close по baseline-выходу).
		/// - useDelayedIntradayStops: управляет уважением к intraday SL в delayed-модели (SlFirst).
		/// - dailyTpPct: дневной TP (по умолчанию 3%).
		/// - dailyStopPct: дневной SL (по умолчанию 5%).
		/// - useAntiDirectionOverlay: переворачивать ли направление сделки (Anti-D) по правилам SL/волатильности.
		/// Все сделки живут в окне [entryUtc; baselineExitUtc),
		/// где baselineExitUtc = следующее NY-утро 08:00 рабочего дня минус 2 минуты.
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
			double dailyStopPct = 0.05,
			bool useAntiDirectionOverlay = false )
			{
			if (candles1m == null || candles1m.Count == 0)
				throw new InvalidOperationException ("[pnl] 1m candles are required for liquidation/TP/SL.");

			// Сортируем минутки по времени, чтобы дальнейшие срезы работали корректно.
			var m1 = candles1m.OrderBy (m => m.OpenTimeUtc).ToList ();

			// Для быстрого поиска окон по времени (dayStart/dayEnd) собираем
			// отдельный массив timestamp'ов и используем бинарный поиск.
			// Это снимает O(N * days) по Where() на весь m1.
			var m1Times = m1.Select (m => m.OpenTimeUtc).ToList ();

			// Агрегированная статистика по Anti-D overlay за один прогон ComputePnL.
			int antiDChecked = 0;
			int antiDApplied = 0;
			int[] antiDByPredLabel = new int[3];
			var antiDByLev = new Dictionary<double, int> ();

			int antiDMinMoveCount = 0;
			double antiDMinMoveSum = 0.0;
			double antiDMinMoveMin = double.MaxValue;
			double antiDMinMoveMax = 0.0;

			// Журналы результатов
			var resultTrades = new List<PnLTrade> ();
			var resultBySource = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
			var buckets = InitBuckets ();

			double withdrawnLocal = 0.0;
			bool anyLiquidation = false;
			bool globalDead = false;

			// Локальная функция: бинарный поиск "нижней границы":
			// индекс первого элемента >= value.
			static int LowerBound ( List<DateTime> arr, DateTime value )
				{
				int lo = 0;
				int hi = arr.Count;

				while (lo < hi)
					{
					int mid = lo + ((hi - lo) / 2);
					if (arr[mid] < value)
						lo = mid + 1;
					else
						hi = mid;
					}

				return lo;
				}

			// Локальная функция: срез минуток по [startUtc; endUtc) через индексы.
			static List<Candle1m> SliceByTime (
				List<Candle1m> all,
				List<DateTime> times,
				DateTime startUtc,
				DateTime endUtc )
				{
				if (all.Count == 0) return new List<Candle1m> ();
				if (startUtc >= endUtc) return new List<Candle1m> ();

				var startIdx = LowerBound (times, startUtc);
				var endIdx = LowerBound (times, endUtc); // правая граница, не включительно

				if (startIdx >= all.Count || startIdx >= endIdx)
					return new List<Candle1m> ();

				var count = endIdx - startIdx;
				return all.GetRange (startIdx, count);
				}

			// ЛОКАЛЬНАЯ ФУНКЦИЯ: регистрация трейда + обновление бакета/метрик.
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

				if (!buckets.TryGetValue (bucketName, out var bucket))
					throw new InvalidOperationException ($"[pnl] unknown bucket '{bucketName}'.");

				if (bucket.IsDead) return;

				if (leverage <= 0.0)
					throw new InvalidOperationException ("[pnl] leverage must be positive in RegisterTrade().");

				if (positionFraction <= 0.0)
					throw new InvalidOperationException ("[pnl] positionFraction must be positive in RegisterTrade().");

				if (entryPrice <= 0.0)
					throw new InvalidOperationException ("[pnl] entryPrice must be positive in RegisterTrade().");

				if (tradeMinutes == null || tradeMinutes.Count == 0)
					throw new InvalidOperationException ("[pnl] tradeMinutes must not be empty in RegisterTrade().");

				double targetPosBase = bucket.BaseCapital * positionFraction;
				if (targetPosBase <= 0.0)
					throw new InvalidOperationException ("[pnl] targetPosBase must be positive in RegisterTrade().");

				double availableEquity = bucket.Equity;
				if (availableEquity <= 0.0)
					throw new InvalidOperationException ("[pnl] availableEquity must be positive for opening a trade.");

				double marginUsed = Math.Min (targetPosBase, availableEquity);
				if (marginUsed <= 0.0)
					throw new InvalidOperationException ("[pnl] marginUsed must be positive in RegisterTrade().");

				double liqPriceTheoretical = ComputeLiqPrice (entryPrice, isLong, leverage);
				double liqPriceBacktest = ComputeBacktestLiqPrice (entryPrice, isLong, leverage);

				var (liqHit, liqExit) = CheckLiquidation (entryPrice, isLong, leverage, tradeMinutes);
				bool priceLiquidated = liqHit;
				double finalExitPrice = liqHit ? liqExit : exitPrice;

				finalExitPrice = CapWorseThanLiquidation (entryPrice, isLong, leverage, finalExitPrice, out bool forceLiq);
				if (forceLiq) priceLiquidated = true;

				var (mae, mfe) = ComputeMaeMfe (entryPrice, isLong, tradeMinutes);

				double relMove = isLong
					? (finalExitPrice - entryPrice) / entryPrice
					: (entryPrice - finalExitPrice) / entryPrice;

				double notional = marginUsed * leverage;
				double positionPnl = relMove * leverage * marginUsed;
				double positionComm = notional * CommissionRate * 2.0;

				UpdateBucketEquity (
					marginMode,
					bucket,
					marginUsed,
					positionPnl,
					positionComm,
					priceLiquidated,
					ref withdrawnLocal,
					out bool diedThisTrade);

				if (diedThisTrade)
					{
					anyLiquidation = true;

					if (marginMode == MarginMode.Cross)
						{
						globalDead = true;
						}
					else
						{
						if (buckets.Values.All (b => b.IsDead))
							{
							globalDead = true;
							}
						}
					}

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
					MarginUsed = marginUsed,
					PositionUsd = marginUsed,
					GrossReturnPct = Math.Round (relMove * 100.0, 4),
					NetReturnPct = Math.Round ((positionPnl - positionComm) / marginUsed * 100.0, 4),
					Commission = Math.Round (positionComm, 4),
					EquityAfter = Math.Round (bucket.Equity, 2),
					IsLiquidated = priceLiquidated || diedThisTrade,
					IsRealLiquidation = priceLiquidated,
					LiqPrice = liqPriceTheoretical,
					LiqPriceBacktest = liqPriceBacktest,
					MaxAdversePct = Math.Round (mae * 100.0, 4),
					MaxFavorablePct = Math.Round (mfe * 100.0, 4),
					LeverageUsed = leverage,
					});

				if (!resultBySource.TryGetValue (source, out var cnt))
					resultBySource[source] = 1;
				else
					resultBySource[source] = cnt + 1;
				}

			// ===== Основной цикл по "дневным" записям PredictionRecord =====
			foreach (var rec in records.OrderBy (r => r.DateUtc))
				{
				if (globalDead) break;

				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort) continue;

				if (TradeSkipRules.ShouldSkipDay (rec, policy))
					continue;

				double lev = policy.ResolveLeverage (rec);
				if (double.IsNaN (lev) || double.IsInfinity (lev) || lev <= 0.0)
					{
					throw new InvalidOperationException (
						$"[pnl] leverage policy '{policy.Name}' returned invalid leverage={lev} at {rec.DateUtc:yyyy-MM-dd}.");
					}

				if (useAntiDirectionOverlay)
					{
					antiDChecked++;

					bool applyAnti = ShouldApplyAntiDirection (rec, lev);

					if (applyAnti)
						{
						antiDApplied++;

						if (rec.PredLabel is >= 0 and <= 2)
							{
							antiDByPredLabel[rec.PredLabel]++;
							}

						if (!antiDByLev.TryGetValue (lev, out var cnt))
							cnt = 0;
						antiDByLev[lev] = cnt + 1;

						var mm = rec.MinMove;
						if (!double.IsNaN (mm) && mm > 0.0)
							{
							antiDMinMoveCount++;
							antiDMinMoveSum += mm;

							if (mm < antiDMinMoveMin) antiDMinMoveMin = mm;
							if (mm > antiDMinMoveMax) antiDMinMoveMax = mm;
							}

						bool tmp = goLong;
						goLong = goShort;
						goShort = tmp;
						rec.AntiDirectionApplied = true;
						}
					}

				DateTime dayStart = rec.DateUtc;
				DateTime dayEnd = Windowing.ComputeBaselineExitUtc (dayStart);

				// Оптимизированный срез: вместо Where() по всему m1,
				// берём диапазон индексов через бинарный поиск по времени.
				var dayMinutes = SliceByTime (m1, m1Times, dayStart, dayEnd);

				if (dayMinutes.Count == 0)
					throw new InvalidOperationException (
						$"[pnl] 1m candles missing for PnL window starting {rec.DateUtc:yyyy-MM-dd}");

				double entry = rec.Entry;
				if (entry <= 0.0)
					throw new InvalidOperationException (
						$"[pnl] PredictionRecord.Entry must be positive at {rec.DateUtc:yyyy-MM-dd}.");

				// ===== DAILY (TP/SL/Close по baseline-окну) =====
					{
					double slPct = useDailyStopLoss ? dailyStopPct : 0.0;

					var (exitPrice, exitTimeUtc) = TryHitDailyExit (
						entry,
						goLong,
						dailyTpPct,
						slPct,
						dayMinutes,
						dayEnd
					);

					RegisterTrade (
						rec.DateUtc,
						rec.DateUtc,
						exitTimeUtc,
						"Daily",
						"daily",
						DailyPositionFraction,
						isLong: goLong,
						entryPrice: entry,
						exitPrice: exitPrice,
						leverage: lev,
						tradeMinutes: dayMinutes
					);
					}

				if (globalDead) break;

				// ===== DELAYED (отложенный вход + intraday-результат) =====
				if (!string.IsNullOrEmpty (rec.DelayedSource) && rec.DelayedEntryExecuted)
					{
					bool dLong = goLong;
					double dEntry = rec.DelayedEntryPrice;
					if (dEntry <= 0.0)
						throw new InvalidOperationException (
							$"[pnl] DelayedEntryPrice must be positive at {rec.DateUtc:yyyy-MM-dd}.");

					DateTime delayedEntryTime = rec.DelayedEntryExecutedAtUtc ?? rec.DateUtc;

					var delayedMinutes = dayMinutes
						.Where (m => m.OpenTimeUtc >= delayedEntryTime)
						.ToList ();

					if (delayedMinutes.Count == 0)
						throw new InvalidOperationException (
							$"[pnl] delayed 1m candles missing for PnL window starting {rec.DateUtc:yyyy-MM-dd}");

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
						var last = delayedMinutes.Last ();
						dExit = last.Close;
						delayedExitTime = dayEnd;
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

			if (useAntiDirectionOverlay && antiDChecked > 0)
				{
				double appliedPct = (double) antiDApplied / antiDChecked * 100.0;

				Console.WriteLine (
					"[anti-d][summary] policy={0}, checked={1}, applied={2} ({3:0.0}%)",
					policy.Name,
					antiDChecked,
					antiDApplied,
					appliedPct
				);

				int lbl0 = antiDByPredLabel[0];
				int lbl1 = antiDByPredLabel[1];
				int lbl2 = antiDByPredLabel[2];

				Console.WriteLine (
					"[anti-d][labels] label0={0}, label1={1}, label2={2}",
					lbl0, lbl1, lbl2
				);

				if (antiDByLev.Count > 0)
					{
					var parts = antiDByLev
						.OrderBy (kv => kv.Key)
						.Select (kv => $"{kv.Key:0.##}x:{kv.Value}");

					Console.WriteLine ("[anti-d][by-lev] " + string.Join (", ", parts));
					}

				if (antiDMinMoveCount > 0)
					{
					double avgMm = antiDMinMoveSum / antiDMinMoveCount;
					Console.WriteLine (
						"[anti-d][minMove] count={0}, avg={1:0.000}, min={2:0.000}, max={3:0.000}",
						antiDMinMoveCount,
						avgMm,
						antiDMinMoveMin,
						antiDMinMoveMax
					);
					}
				}
			}
		}
	}
