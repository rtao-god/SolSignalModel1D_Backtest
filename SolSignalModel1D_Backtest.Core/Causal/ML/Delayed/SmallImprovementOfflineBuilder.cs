using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Delayed
	{
	public static class SmallImprovementOfflineBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		private static readonly double[] ShallowFactors = new[] { 0.12, 0.18, 0.24 };
		private const double ShallowMaxDelayHours = 2.0;

		public static List<SmallImprovementSample> Build (
			List<BacktestRecord> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var res = new List<SmallImprovementSample> ((rows?.Count ?? 0) * 4);
			if (rows == null || rows.Count == 0 || sol1h == null || sol1h.Count == 0)
				return res;

			foreach (var r in rows)
				{
				if (!sol6hDict.TryGetValue (r.ToCausalDateUtc(), out var day6))
					continue;

				double entry = day6.Close;
				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0 || double.IsNaN (dayMinMove) || double.IsInfinity (dayMinMove))
					throw new InvalidOperationException (
						$"[sl-offline] invalid MinMove for {r.ToCausalDateUtc():O}: {dayMinMove}. " +
						"MinMove должен быть > 0, стоит проверить MinMoveEngine/RowBuilder.");

				DateTime entryUtc = r.ToCausalDateUtc();
				DateTime endUtc;
				try { endUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz); }
				catch (Exception ex)
					{
					// Явно ломаемся с понятным сообщением
					throw new InvalidOperationException (
						$"Failed to compute baseline exit for entryUtc={entryUtc:o}, tz={NyTz.Id}. " +
						"Fix data/windowing logic instead of relying on fallback.",
						ex);
					}

				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= entryUtc && h.OpenTimeUtc < endUtc)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();
				if (dayHours.Count == 0)
					continue;

				BuildForDir (res, r, dayHours, sol1h, entry, dayMinMove, true, NyTz);
				BuildForDir (res, r, dayHours, sol1h, entry, dayMinMove, false, NyTz);
				}

			return res;
			}

		private static void BuildForDir (
			List<SmallImprovementSample> sink,
			BacktestRecord r,
			List<Candle1h> dayHours,
			IReadOnlyList<Candle1h> allHours,
			double entryPrice,
			double dayMinMove,
			bool goLong,
			TimeZoneInfo nyTz )
			{
			bool goShort = !goLong;
			bool strong = true;

			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				dayHours, r.ToCausalDateUtc(), goLong, goShort, entryPrice, dayMinMove, strong, nyTz);

			if (baseOutcome.Result != HourlyTradeResult.SlFirst)
				return;

			foreach (var f in ShallowFactors)
				{
				var delayed = DelayedEntryEvaluator.Evaluate (
					dayHours, r.ToCausalDateUtc(), goLong, goShort, entryPrice, dayMinMove, strong, f, ShallowMaxDelayHours);

				bool label = false;

				if (delayed.Executed)
					{
					int baseRank = RankHourly (baseOutcome.Result);
					int delayedRank = RankDelayed (delayed.Result);

					if (delayedRank >= baseRank)
						label = true;
					else if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
							 delayed.Result == DelayedIntradayResult.SlFirst &&
							 delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
							 delayed.SlPct < baseOutcome.SlPct)
						label = true;
					}

				var feats = TargetLevelFeatureBuilder.Build (
					r.ToCausalDateUtc(), goLong, strong, dayMinMove, entryPrice, allHours);

				sink.Add (new SmallImprovementSample
					{
					Label = label,
					Features = feats,
					EntryUtc = r.ToCausalDateUtc()
					});
				}
			}

		private static int RankHourly ( HourlyTradeResult res ) => res switch
			{
				HourlyTradeResult.TpFirst => 3,
				HourlyTradeResult.None => 2,
				HourlyTradeResult.Ambiguous => 2,
				HourlyTradeResult.SlFirst => 0,
				_ => 0
				};

		private static int RankDelayed ( DelayedIntradayResult res ) => res switch
			{
				DelayedIntradayResult.TpFirst => 3,
				DelayedIntradayResult.None => 2,
				DelayedIntradayResult.Ambiguous => 2,
				DelayedIntradayResult.SlFirst => 0,
				_ => 0
				};
		}
	}
