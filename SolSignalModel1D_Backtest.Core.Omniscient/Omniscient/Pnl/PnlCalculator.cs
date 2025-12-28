using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage;
using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	/// <summary>
	/// PnL-движок: дневной вход (TP/SL/close), delayed A/B, ликвидации по 1m.
	///
	/// Архитектурные инварианты:
	/// - Решения (плечо/скипы/anti-D/направление) берутся ТОЛЬКО из causal-слоя (rec.Causal).
	/// - Forward (DayMinutes/Entry/TrueLabel) используется только для симуляции исполнения, а не для принятия решений.
	/// - Любые несогласованности окон/данных не маскируются фолбэками — это pipeline-bug и должно падать.
	/// </summary>
	public static partial class PnlCalculator
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		// Комиссия taker (в обе стороны). Одна константа, чтобы не расползалось по коду.
		private const double CommissionRate = 0.0004;

		// "Капитал аккаунта" для метрик.
		private const double TotalCapital = 20000.0;

		// Доли бакетов.
		private const double DailyShare = 0.60;
		private const double IntradayShare = 0.25;
		private const double DelayedShare = 0.15;

		// Фракция бакета, которая используется как margin в сделке (до плеча).
		private const double DailyPositionFraction = 1.0;
		private const double IntradayPositionFraction = 0.0;
		private const double DelayedPositionFraction = 0.4;

		/// <summary>
		/// Основной PnL-метод.
		/// Требование к данным: rec.Forward.DayMinutes содержит baseline 1m-окно.
		/// </summary>
		public static void ComputePnL (
			IReadOnlyList<BacktestRecord> records,
			ICausalLeveragePolicy policy,
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

			var resultTrades = new List<PnLTrade> (capacity: records.Count * 2);
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
				IReadOnlyList<Candle1m> tradeMinutes,
				bool isRealLiquidation )
				{
				if (globalDead) return;

				if (!buckets.TryGetValue (bucketName, out var bucket))
					throw new InvalidOperationException ($"[pnl] unknown bucket '{bucketName}'.");

				if (bucket.IsDead) return;

				if (leverage <= 0.0)
					throw new InvalidOperationException ("[pnl] leverage must be > 0 in RegisterTrade().");

				if (positionFraction <= 0.0)
					throw new InvalidOperationException ("[pnl] positionFraction must be > 0 in RegisterTrade().");

				if (entryPrice <= 0.0)
					throw new InvalidOperationException ("[pnl] entryPrice must be > 0 in RegisterTrade().");

				if (tradeMinutes == null || tradeMinutes.Count == 0)
					throw new InvalidOperationException ("[pnl] tradeMinutes must not be empty in RegisterTrade().");

				// Размер маржи, который пытаемся использовать из бакета.
				double targetPosBase = bucket.BaseCapital * positionFraction;
				if (targetPosBase <= 0.0)
					throw new InvalidOperationException ("[pnl] targetPosBase must be > 0 in RegisterTrade().");

				double availableEquity = bucket.Equity;
				if (availableEquity <= 0.0)
					throw new InvalidOperationException ("[pnl] bucket equity must be > 0 to open a trade.");

				double marginUsed = Math.Min (targetPosBase, availableEquity);
				if (marginUsed <= 0.0)
					throw new InvalidOperationException ("[pnl] marginUsed must be > 0 in RegisterTrade().");

				double liqPriceTheory = ComputeLiqPrice (entryPrice, isLong, leverage);
				double liqPriceBacktest = ComputeBacktestLiqPrice (entryPrice, isLong, leverage);

				// В случае неконсистентных TP/SL данных принудительно капаем хуже ликвидации.
				double finalExitPrice = CapWorseThanLiquidation (
					entryPrice, isLong, leverage, exitPrice, out bool forcedLiq);

				bool priceLiquidated = isRealLiquidation || forcedLiq;

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
						// Cross: смерть бакета трактуем как смерть всего аккаунта (строгий режим).
						globalDead = true;
						}
					else
						{
						// Isolated: аккаунт мёртв, когда умерли все бакеты.
						if (buckets.Values.All (b => b.IsDead))
							globalDead = true;
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
					LiqPrice = liqPriceTheory,
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

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] == null)
                    throw new InvalidOperationException($"[pnl] records[{i}] is null BacktestRecord item.");
                if (records[i]!.Causal == null)
                    throw new InvalidOperationException($"[pnl] records[{i}].Causal is null — causal layer missing.");
                if (records[i]!.Forward == null)
                    throw new InvalidOperationException($"[pnl] records[{i}].Forward is null — forward layer missing.");
            }

            foreach (var rec in records.OrderBy(r => r.Causal!.EntryUtc.Value))
            {
                if (globalDead) break;

                var causal = rec.Causal ?? throw new InvalidOperationException("[pnl] rec.Causal is null — causal layer missing.");

                DateTime dayStart = RequireUtcDayStart(causal.EntryDayKeyUtc.Value, "EntryDayKeyUtc");
                DateTime dayEnd = GetBaselineWindowEndUtcOrFail(rec, dayStart, NyTz);

                var dayMinutes = rec.Forward.DayMinutes;
                if (dayMinutes == null || dayMinutes.Count == 0)
                    throw new InvalidOperationException($"[pnl] Forward.DayMinutes is empty for {dayStart:yyyy-MM-dd}.");

                DateTime entryTimeUtc = causal.EntryUtc.Value;
                if (entryTimeUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException(
                        $"[pnl] Causal.EntryUtc must be UTC for {dayStart:yyyy-MM-dd}: {entryTimeUtc:O} (Kind={entryTimeUtc.Kind}).");

                if (dayMinutes[0].OpenTimeUtc != entryTimeUtc)
                    throw new InvalidOperationException(
                        $"[pnl] Forward.DayMinutes[0].OpenTimeUtc != Causal.EntryUtc at {dayStart:yyyy-MM-dd}. " +
                        $"firstMinute={dayMinutes[0].OpenTimeUtc:O}, entryUtc={entryTimeUtc:O}.");


                double entry = rec.Entry;
				if (entry <= 0.0)
					throw new InvalidOperationException ($"[pnl] Forward.Entry must be > 0 for {dayStart:yyyy-MM-dd}.");

				// Направление из предсказаний (causal-решение).
				if (!TryResolveDirection (rec, predictionMode, out bool goLong, out bool goShort))
					continue;

				// Политика может фильтровать дни (но фильтры должны быть только по causal).
				if (TradeSkipRules.ShouldSkipDay (rec, policy))
					continue;

				// Плечо: строго causal.
				double lev = policy.ResolveLeverage (rec.Causal);
				if (double.IsNaN (lev) || double.IsInfinity (lev) || lev <= 0.0)
					throw new InvalidOperationException ($"[pnl] leverage policy '{policy.Name}' returned invalid lev={lev} for {dayStart:yyyy-MM-dd}.");

				// Anti-direction overlay (строго по causal).
				if (useAntiDirectionOverlay)
					{
					antiDChecked++;

					bool applyAnti = ShouldApplyAntiDirection (rec, lev);

					if (applyAnti)
						{
						antiDApplied++;

						if (rec.PredLabel is >= 0 and <= 2)
							antiDByPredLabel[rec.PredLabel]++;

						if (!antiDByLev.TryGetValue (lev, out var c)) c = 0;
						antiDByLev[lev] = c + 1;

						double mm = rec.MinMove;
						if (!double.IsNaN (mm) && mm > 0.0)
							{
							antiDMinMoveCount++;
							antiDMinMoveSum += mm;
							if (mm < antiDMinMoveMin) antiDMinMoveMin = mm;
							if (mm > antiDMinMoveMax) antiDMinMoveMax = mm;
							}

						(goLong, goShort) = (goShort, goLong);
						rec.AntiDirectionApplied = true;
						}
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

					var (liqHit, _) = CheckLiquidation (entry, goLong, lev, dayMinutes);

                    RegisterTrade(
						dayStart,
						entryTimeUtc,
						exitTimeUtc,
						"Daily",
						"daily",
						DailyPositionFraction,
						isLong: goLong,
						entryPrice: entry,
						exitPrice: exitPrice,
						leverage: lev,
						tradeMinutes: dayMinutes,
						isRealLiquidation: liqHit
					);

                }

                if (globalDead) break;

				// ===== DELAYED =====
				if (!string.IsNullOrEmpty (rec.DelayedSource) && rec.DelayedExecution is { } exec)
					{
					bool dLong = goLong;

					double dEntry = exec.EntryPrice;

                    DateTime delayedEntryTime = exec.ExecutedAtUtc;
                    if (delayedEntryTime < entryTimeUtc || delayedEntryTime >= dayEnd)
                        throw new InvalidOperationException(
                            $"[pnl] Delayed executedAtUtc={delayedEntryTime:O} is outside baseline window {entryTimeUtc:O}..{dayEnd:O}.");

                    int delayedStartIndex = FindFirstMinuteIndexAtOrAfter (dayMinutes, delayedEntryTime);
					if (delayedStartIndex < 0)
						throw new InvalidOperationException ($"[pnl] cannot find 1m candles for delayed window starting {delayedEntryTime:O}.");

					var delayedMinutes = new TradeMinutesSlice (dayMinutes, delayedStartIndex);

					double dExit;
					DateTime delayedExitTime;

					var delayedRes = exec.IntradayResult;

					// Если слой сказал TpFirst/SlFirst — обязаны найти реальное пересечение в 1m.
					if (delayedRes == DelayedIntradayResult.TpFirst)
						{
						double tpPctD = rec.DelayedIntradayTpPct
							?? throw new InvalidOperationException ($"[pnl] DelayedIntradayTpPct is null for TpFirst at {dayStart:yyyy-MM-dd}.");

						double tp = dLong ? dEntry * (1.0 + tpPctD) : dEntry * (1.0 - tpPctD);

						delayedExitTime = FindFirstHitUtcOrFail (delayedMinutes, dLong, HitKind.TakeProfit, tp);
						dExit = tp;
						}
					else if (delayedRes == DelayedIntradayResult.SlFirst && useDelayedIntradayStops)
						{
						double slPctD = rec.DelayedIntradaySlPct
							?? throw new InvalidOperationException ($"[pnl] DelayedIntradaySlPct is null for SlFirst at {dayStart:yyyy-MM-dd}.");

						double sl = dLong ? dEntry * (1.0 - slPctD) : dEntry * (1.0 + slPctD);

						delayedExitTime = FindFirstHitUtcOrFail (delayedMinutes, dLong, HitKind.StopLoss, sl);
						dExit = sl;
						}
					else
						{
						var last = delayedMinutes[delayedMinutes.Count - 1];
						dExit = last.Close;
						delayedExitTime = dayEnd;
						}

					var (liqHitD, _) = CheckLiquidation (dEntry, dLong, lev, delayedMinutes);

					string src = rec.DelayedSource == "A" ? "DelayedA" : "DelayedB";

					RegisterTrade (
						dayStart,
						delayedEntryTime,
						delayedExitTime,
						src,
						"delayed",
						DelayedPositionFraction,
						dLong,
						dEntry,
						dExit,
						lev,
						delayedMinutes,
						isRealLiquidation: liqHitD
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

			bucketSnapshots = buckets.Values
				.Select (b => new PnlBucketSnapshot
					{
					Name = b.Name,
					StartCapital = b.BaseCapital,
					EquityNow = b.Equity,
					Withdrawn = b.Withdrawn
					})
				.ToList ();

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

				Console.WriteLine (
					"[anti-d][labels] label0={0}, label1={1}, label2={2}",
					antiDByPredLabel[0], antiDByPredLabel[1], antiDByPredLabel[2]
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
						antiDMinMoveCount, avgMm, antiDMinMoveMin, antiDMinMoveMax
					);
					}
				}
			}
		}
	}
