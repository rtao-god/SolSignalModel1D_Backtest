using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// PnL-движок: дневной вход (TP/SL/close), delayed A/B, ликвидации по 1m,
	/// Cross/Isolated, бакеты капитала и вывод средств.
	/// Работает поверх BacktestRecord, где уже собраны causal-прогноз и forward-путь.
	/// </summary>
	public static partial class PnlCalculator
		{
		private const double CommissionRate = 0.0004;
		private const double TotalCapital = 20000.0;

		private const double DailyShare = 0.60;
		private const double IntradayShare = 0.25;
		private const double DelayedShare = 0.15;

		private const double DailyPositionFraction = 1.0;
		private const double IntradayPositionFraction = 0.0;
		private const double DelayedPositionFraction = 0.4;

		/// <summary>
		/// Основной PnL-метод.
		/// Принимает последовательность BacktestRecord, где Forward.DayMinutes уже
		/// содержит baseline-1m-окно, и конфигурацию плеча/SL/TP.
		/// </summary>
		public static void ComputePnL (
			IReadOnlyList<BacktestRecord> records,
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
			bool useAntiDirectionOverlay = false,
			PnlPredictionMode predictionMode = PnlPredictionMode.DayOnly )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (policy == null) throw new ArgumentNullException (nameof (policy));

			if (records.Count == 0)
				{
				trades = new List<PnLTrade> ();
				tradesBySource = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
				withdrawnTotal = 0.0;
				bucketSnapshots = new List<PnlBucketSnapshot> ();
				totalPnlPct = 0.0;
				maxDdPct = 0.0;
				hadLiquidation = false;
				return;
				}

			int antiDChecked = 0;
			int antiDApplied = 0;
			int[] antiDByPredLabel = new int[3];
			var antiDByLev = new Dictionary<double, int> ();

			int antiDMinMoveCount = 0;
			double antiDMinMoveSum = 0.0;
			double antiDMinMoveMin = double.MaxValue;
			double antiDMinMoveMax = 0.0;

			var resultTrades = new List<PnLTrade> ();
			var resultBySource = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
			var buckets = InitBuckets ();

			double withdrawnLocal = 0.0;
			bool anyLiquidation = false;
			bool globalDead = false;

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
					throw new InvalidOperationException ($"[pnl] неизвестный бакет '{bucketName}'.");

				if (bucket.IsDead) return;

				if (leverage <= 0.0)
					throw new InvalidOperationException ("[pnl] leverage должен быть > 0 в RegisterTrade().");

				if (positionFraction <= 0.0)
					throw new InvalidOperationException ("[pnl] positionFraction должен быть > 0 в RegisterTrade().");

				if (entryPrice <= 0.0)
					throw new InvalidOperationException ("[pnl] entryPrice должен быть > 0 в RegisterTrade().");

				if (tradeMinutes == null || tradeMinutes.Count == 0)
					throw new InvalidOperationException ("[pnl] tradeMinutes не должен быть пустым в RegisterTrade().");

				double targetPosBase = bucket.BaseCapital * positionFraction;
				if (targetPosBase <= 0.0)
					throw new InvalidOperationException ("[pnl] targetPosBase должен быть > 0 в RegisterTrade().");

				double availableEquity = bucket.Equity;
				if (availableEquity <= 0.0)
					throw new InvalidOperationException ("[pnl] availableEquity должен быть > 0 для открытия сделки.");

				double marginUsed = Math.Min (targetPosBase, availableEquity);
				if (marginUsed <= 0.0)
					throw new InvalidOperationException ("[pnl] marginUsed должен быть > 0 в RegisterTrade().");

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
					LeverageUsed = leverage
					});

				if (!resultBySource.TryGetValue (source, out var cnt))
					resultBySource[source] = 1;
				else
					resultBySource[source] = cnt + 1;
				}

			// ===== Основной цикл по BacktestRecord =====
			foreach (var rec in records.OrderBy (r => r.DateUtc))
				{
				if (globalDead) break;

				var dayMinutes = rec.Forward.DayMinutes;
				if (dayMinutes == null || dayMinutes.Count == 0)
					{
					throw new InvalidOperationException (
						$"[pnl] Forward.DayMinutes пуст для окна, начинающегося {rec.DateUtc:yyyy-MM-dd}.");
					}

				double entry = rec.Entry;
				if (entry <= 0.0)
					{
					throw new InvalidOperationException (
						$"[pnl] Forward.Entry должен быть > 0 для {rec.DateUtc:yyyy-MM-dd}.");
					}

				bool goLong;
				bool goShort;

				switch (predictionMode)
					{
					case PnlPredictionMode.DayOnly:
							{
							goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
							goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
							break;
							}
					case PnlPredictionMode.DayPlusMicro:
							{
							int cls = rec.PredLabel_DayMicro;
							goLong = cls == 2;
							goShort = cls == 0;
							break;
							}
					case PnlPredictionMode.DayPlusMicroPlusSl:
							{
							double up = rec.ProbUp_Total;
							double down = rec.ProbDown_Total;
							double flat = rec.ProbFlat_Total;

							goLong = up > down && up > flat;
							goShort = down > up && down > flat;
							break;
							}
					default:
						throw new ArgumentOutOfRangeException (nameof (predictionMode), predictionMode, "Unknown prediction mode");
					}

				if (!goLong && !goShort)
					continue;

				if (TradeSkipRules.ShouldSkipDay (rec, policy))
					continue;

				double lev = policy.ResolveLeverage (rec);
				if (double.IsNaN (lev) || double.IsInfinity (lev) || lev <= 0.0)
					{
					throw new InvalidOperationException (
						$"[pnl] политика плеча '{policy.Name}' вернула некорректное значение leverage={lev} на {rec.DateUtc:yyyy-MM-dd}.");
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

				var dayStart = rec.DateUtc;
				var dayEnd = rec.Forward.WindowEndUtc;

				if (dayEnd <= dayStart)
					{
					dayEnd = Windowing.ComputeBaselineExitUtc (dayStart);
					}

				// ===== DAILY =====
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

				// ===== DELAYED =====
				if (!string.IsNullOrEmpty (rec.DelayedSource) && rec.DelayedEntryExecuted)
					{
					bool dLong = goLong;
					double dEntry = rec.DelayedEntryPrice;
					if (dEntry <= 0.0)
						throw new InvalidOperationException (
							$"[pnl] DelayedEntryPrice должен быть > 0 на {rec.DateUtc:yyyy-MM-dd}.");

					DateTime delayedEntryTime = rec.DelayedEntryExecutedAtUtc ?? rec.DateUtc;

					var delayedMinutes = dayMinutes
						.Where (m => m.OpenTimeUtc >= delayedEntryTime)
						.ToList ();

					if (delayedMinutes.Count == 0)
						throw new InvalidOperationException (
							$"[pnl] не найдены 1m-свечи для delayed-окна, начинающегося {rec.DateUtc:yyyy-MM-dd}.");

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
