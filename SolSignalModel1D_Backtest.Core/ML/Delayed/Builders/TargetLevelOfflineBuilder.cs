using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Builders
	{
	public static class TargetLevelOfflineBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		private const double DeepDelayFactor = 0.35;
		private const double DeepMaxDelayHours = 4.0;
		private const double ShallowDelayFactor = 0.15;
		private const double ShallowMaxDelayHours = 2.0;

		public static List<TargetLevelSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var result = new List<TargetLevelSample> (rows.Count * 2);
			if (rows == null || rows.Count == 0 || sol1h == null || sol1h.Count == 0)
				return result;

			foreach (var r in rows)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var dayCandle))
					continue;

				double entryPrice = dayCandle.Close;
				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0) dayMinMove = 0.02;

				DateTime entryUtc = r.Date;
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

				BuildForDir (result, r, dayHours, sol1h, entryPrice, dayMinMove, true, NyTz);
				BuildForDir (result, r, dayHours, sol1h, entryPrice, dayMinMove, false, NyTz);
				}

			return result;
			}

		private static void BuildForDir (
			List<TargetLevelSample> sink,
			DataRow r,
			List<Candle1h> dayHours,
			IReadOnlyList<Candle1h> allHours,
			double entryPrice,
			double dayMinMove,
			bool goLong,
			TimeZoneInfo nyTz )
			{
			bool strongSignal = true;

			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				dayHours, r.Date, goLong, !goLong, entryPrice, dayMinMove, strongSignal, nyTz);

			var deepDelayed = DelayedEntryEvaluator.Evaluate (
				dayHours, r.Date, goLong, !goLong, entryPrice, dayMinMove, strongSignal, DeepDelayFactor, DeepMaxDelayHours);

			var shDelayed = DelayedEntryEvaluator.Evaluate (
				dayHours, r.Date, goLong, !goLong, entryPrice, dayMinMove, strongSignal, ShallowDelayFactor, ShallowMaxDelayHours);

			int label = 0;
			if (deepDelayed.Executed && IsDeepImprovement (baseOutcome, deepDelayed))
				label = 2;
			else if (shDelayed.Executed && IsShallowNotWorse (baseOutcome, shDelayed))
				label = 1;

			var feats = TargetLevelFeatureBuilder.Build (
				r.Date, goLong, strongSignal, dayMinMove, entryPrice, allHours);

			sink.Add (new TargetLevelSample
				{
				Label = label,
				Features = feats,
				EntryUtc = r.Date
				});
			}

		private static bool IsDeepImprovement ( HourlyTradeOutcome baseOutcome, DelayedEntryResult delayed )
			{
			if ((baseOutcome.Result == HourlyTradeResult.SlFirst || baseOutcome.Result == HourlyTradeResult.None) &&
				delayed.Result == DelayedIntradayResult.TpFirst)
				return true;

			if (baseOutcome.Result == HourlyTradeResult.TpFirst &&
				delayed.Result == DelayedIntradayResult.TpFirst)
				return true;

			if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
				delayed.Result == DelayedIntradayResult.SlFirst &&
				delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
				delayed.SlPct < baseOutcome.SlPct)
				return true;

			return false;
			}

		private static bool IsShallowNotWorse ( HourlyTradeOutcome baseOutcome, DelayedEntryResult delayed )
			{
			int baseRank = RankHourly (baseOutcome.Result);
			int delayedRank = RankDelayed (delayed.Result);

			if (delayedRank < baseRank) return false;

			if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
				delayed.Result == DelayedIntradayResult.SlFirst &&
				delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
				delayed.SlPct < baseOutcome.SlPct)
				return true;

			if (delayed.Result == DelayedIntradayResult.None &&
				baseOutcome.Result == HourlyTradeResult.SlFirst)
				return true;

			return delayedRank >= baseRank;
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
