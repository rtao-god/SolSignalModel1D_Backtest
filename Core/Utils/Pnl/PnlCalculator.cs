using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	public static class PnlCalculator
		{
		private const double CommissionRate = 0.0004;
		private const double TotalCapital = 20000.0;

		// разбиение 20k
		private const double DailyShare = 0.60;
		private const double IntradayShare = 0.25;
		private const double DelayedShare = 0.15;

		// внутри корзины
		private const double DailyPositionFraction = 1.0;
		private const double IntradayPositionFraction = 0.0;
		private const double DelayedPositionFraction = 0.4;

		private const double DailyStopPct = 0.05;

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

		public static void ComputePnL (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1h>? candles1h,
			ILeveragePolicy policy,
			MarginMode marginMode,
			out List<PnLTrade> trades,
			out double totalPnlPct,
			out double maxDdPct,
			out Dictionary<string, int> tradesBySource,
			out double withdrawnTotal,
			out List<PnlBucketSnapshot> bucketSnapshots,
			out bool hadLiquidation )
			{
			var resultTrades = new List<PnLTrade> ();
			var resultBySource = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
			double withdrawnLocal = 0.0;
			bool anyLiquidation = false;

			var hours = (candles1h ?? Array.Empty<Candle1h> ())
				.OrderBy (h => h.OpenTimeUtc)
				.ToList ();

			var buckets = new Dictionary<string, BucketState> (StringComparer.OrdinalIgnoreCase)
				{
				["daily"] = MakeBucket ("daily", TotalCapital * DailyShare),
				["intraday"] = MakeBucket ("intraday", TotalCapital * IntradayShare),
				["delayed"] = MakeBucket ("delayed", TotalCapital * DelayedShare),
				};

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
				List<Candle1h> tradeBars )
				{
				if (globalDead)
					return;

				if (!buckets.TryGetValue (bucketName, out var bucket))
					return;
				if (bucket.IsDead)
					return;
				if (leverage <= 0.0)
					return;
				if (positionFraction <= 0.0)
					return;

				double posBase = bucket.BaseCapital * positionFraction;
				if (posBase <= 0.0)
					return;

				bool priceLiquidated = false;
				double finalExitPrice = exitPrice;

				// ликвидация по пути
				if (tradeBars.Count > 0)
					{
					double maxAdverseAllowed = 1.0 / leverage;
					if (isLong)
						{
						double minLow = tradeBars.Min (b => b.Low);
						double adverse = (entryPrice - minLow) / entryPrice;
						if (adverse >= maxAdverseAllowed)
							{
							finalExitPrice = entryPrice * (1.0 - maxAdverseAllowed);
							priceLiquidated = true;
							}
						}
					else
						{
						double maxHigh = tradeBars.Max (b => b.High);
						double adverse = (maxHigh - entryPrice) / entryPrice;
						if (adverse >= maxAdverseAllowed)
							{
							finalExitPrice = entryPrice * (1.0 + maxAdverseAllowed);
							priceLiquidated = true;
							}
						}
					}

				double relMove = isLong
					? (finalExitPrice - entryPrice) / entryPrice
					: (entryPrice - finalExitPrice) / entryPrice;

				double notional = posBase * leverage;
				double positionPnl = relMove * leverage * posBase;
				double positionComm = notional * CommissionRate * 2.0;

				double newEquity = bucket.Equity;

				if (marginMode == MarginMode.Cross)
					{
					if (priceLiquidated)
						{
						newEquity = 0.0;
						bucket.IsDead = true;
						anyLiquidation = true;
						globalDead = true; // cross ликвиднуло — всем конец
						}
					else
						{
						newEquity = bucket.Equity + positionPnl - positionComm;
						}

					if (newEquity < 0) newEquity = 0.0;

					// вывод
					if (!globalDead && newEquity > bucket.BaseCapital)
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
						// тут мы потеряли всю маржу и комиссию → это и есть ликвидация
						newEquity = bucket.Equity - posBase - positionComm;
						if (newEquity < 0) newEquity = 0.0;
						bucket.IsDead = true;
						anyLiquidation = true;
						globalDead = true; // ты так и хотел: ликнуло → дальше не торгуем
						}
					else
						{
						newEquity = bucket.Equity + positionPnl - positionComm;
						if (newEquity <= 0.0)
							{
							newEquity = 0.0;
							bucket.IsDead = true;
							anyLiquidation = true; // большой минус из-за плеча → тоже считаем ликвидацией
							globalDead = true;
							}
						}
					}

				// лог
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
					EquityAfter = Math.Round (newEquity, 2),
					IsLiquidated = priceLiquidated || (marginMode == MarginMode.Isolated && newEquity == 0.0),
					LeverageUsed = leverage
					});

				// статистика по source
				if (!resultBySource.TryGetValue (source, out var cnt))
					resultBySource[source] = 1;
				else
					resultBySource[source] = cnt + 1;

				bucket.Equity = newEquity;

				// dd
				double visible = bucket.Equity + bucket.Withdrawn;
				if (visible > bucket.PeakVisible)
					bucket.PeakVisible = visible;

				double dd = bucket.PeakVisible > 1e-9
					? (bucket.PeakVisible - visible) / bucket.PeakVisible
					: 0.0;

				if (dd > bucket.MaxDd) bucket.MaxDd = dd;
				}

			// ===== основной цикл по дням =====
			foreach (var rec in records.OrderBy (r => r.DateUtc))
				{
				if (globalDead) break;

				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				double lev = policy.ResolveLeverage (rec);
				if (lev <= 0.0)
					continue;

				DateTime dayStart = rec.DateUtc;
				DateTime dayEnd = rec.DateUtc.AddHours (24);

				var dayBars = hours
					.Where (h => h.OpenTimeUtc >= dayStart && h.OpenTimeUtc < dayEnd)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();

				double entry = rec.Entry;

				// ===== DAILY =====
					{
					double tpPct = 0.03;
					double slPct = DailyStopPct;
					double exitPrice = entry;
					DateTime entryTimeUtc = rec.DateUtc;
					DateTime exitTimeUtc = rec.DateUtc.AddHours (24);

					bool tpHit = false;
					bool slHit = false;

					if (dayBars.Count > 0)
						{
						if (goLong)
							{
							double tpPrice = entry * (1.0 + tpPct);
							double slPrice = entry * (1.0 - slPct);
							foreach (var bar in dayBars)
								{
								bool hitSl = bar.Low <= slPrice;
								bool hitTp = bar.High >= tpPrice;

								if (hitSl)
									{
									exitPrice = slPrice;
									exitTimeUtc = bar.OpenTimeUtc;
									slHit = true;
									break;
									}
								if (hitTp)
									{
									exitPrice = tpPrice;
									exitTimeUtc = bar.OpenTimeUtc;
									tpHit = true;
									break;
									}
								}
							if (!tpHit && !slHit)
								{
								var last = dayBars.Last ();
								exitPrice = last.Close;
								exitTimeUtc = last.OpenTimeUtc;
								}
							}
						else
							{
							double tpPrice = entry * (1.0 - tpPct);
							double slPrice = entry * (1.0 + slPct);
							foreach (var bar in dayBars)
								{
								bool hitSl = bar.High >= slPrice;
								bool hitTp = bar.Low <= tpPrice;

								if (hitSl)
									{
									exitPrice = slPrice;
									exitTimeUtc = bar.OpenTimeUtc;
									slHit = true;
									break;
									}
								if (hitTp)
									{
									exitPrice = tpPrice;
									exitTimeUtc = bar.OpenTimeUtc;
									tpHit = true;
									break;
									}
								}
							if (!tpHit && !slHit)
								{
								var last = dayBars.Last ();
								exitPrice = last.Close;
								exitTimeUtc = last.OpenTimeUtc;
								}
							}
						}
					else
						{
						// fallback по дневным полям
						exitPrice = goLong
							? (rec.MaxHigh24 >= entry * 1.03 ? entry * 1.03 : rec.Close24)
							: (rec.MinLow24 <= entry * 0.97 ? entry * 0.97 : rec.Close24);
						}

					RegisterTrade (
						rec.DateUtc,
						entryTimeUtc,
						exitTimeUtc,
						"Daily",
						"daily",
						DailyPositionFraction,
						goLong,
						entry,
						exitPrice,
						lev,
						dayBars);
					}

				if (globalDead) break;

				// ===== DELAYED =====
				if (!string.IsNullOrEmpty (rec.DelayedSource) &&
					rec.DelayedEntryExecuted)
					{
					bool dLong = goLong;
					double dEntry = rec.DelayedEntryPrice;
					double dExit = rec.Close24;

					DateTime delayedEntryTime = rec.DelayedEntryExecutedAtUtc ?? rec.DateUtc;
					DateTime delayedExitTime = rec.DateUtc.AddHours (24);

					var delayedBars = dayBars
						.Where (b => b.OpenTimeUtc >= delayedEntryTime)
						.ToList ();

					if (rec.DelayedIntradayResult == (int) DelayedIntradayResult.TpFirst)
						{
						dExit = dLong
							? dEntry * (1.0 + rec.DelayedIntradayTpPct)
							: dEntry * (1.0 - rec.DelayedIntradayTpPct);
						if (delayedBars.Count > 0)
							delayedExitTime = delayedBars.First ().OpenTimeUtc;
						}
					else if (rec.DelayedIntradayResult == (int) DelayedIntradayResult.SlFirst)
						{
						dExit = dLong
							? dEntry * (1.0 - rec.DelayedIntradaySlPct)
							: dEntry * (1.0 + rec.DelayedIntradaySlPct);
						if (delayedBars.Count > 0)
							delayedExitTime = delayedBars.First ().OpenTimeUtc;
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
						delayedBars);
					}

				if (globalDead) break;
				}

			// финал
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

		private static BucketState MakeBucket ( string name, double baseCapital )
			{
			return new BucketState
				{
				Name = name,
				BaseCapital = baseCapital,
				Equity = baseCapital,
				PeakVisible = baseCapital,
				MaxDd = 0.0,
				Withdrawn = 0.0,
				IsDead = false
				};
			}
		}
	}
