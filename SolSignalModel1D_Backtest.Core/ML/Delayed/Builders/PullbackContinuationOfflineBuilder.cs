using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Builders
	{
	public static class PullbackContinuationOfflineBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		private static readonly double[] DeepFactors = new[] { 0.35, 0.45, 0.55 };
		private const double DeepMaxDelayHours = 4.0;

		public static List<PullbackContinuationSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var res = new List<PullbackContinuationSample> (rows?.Count * 4 ?? 0);
			if (rows == null || rows.Count == 0 || sol1h == null || sol1h.Count == 0)
				return res;

			var allHours = sol1h.OrderBy (h => h.OpenTimeUtc).ToList ();

			foreach (var r in rows)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var day6h))
					continue;

				double entry = day6h.Close;
				if (entry <= 0) continue;

				double minMove = r.MinMove;
				if (minMove <= 0) minMove = 0.02;

				DateTime entryUtc = r.Date;
				DateTime endUtc;
				try { endUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz); }
				catch { endUtc = entryUtc.AddHours (24); }

				var dayHours = allHours
					.Where (h => h.OpenTimeUtc >= entryUtc && h.OpenTimeUtc < endUtc)
					.ToList ();

				if (dayHours.Count == 0)
					continue;

				BuildForDir (res, r, dayHours, allHours, entry, minMove, true, NyTz);
				BuildForDir (res, r, dayHours, allHours, entry, minMove, false, NyTz);
				}

			return res;
			}

		private static void BuildForDir (
			List<PullbackContinuationSample> sink,
			DataRow r,
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
				dayHours, r.Date, goLong, goShort, entryPrice, dayMinMove, strong, nyTz);

			if (baseOutcome.Result != HourlyTradeResult.SlFirst)
				return;

			foreach (var f in DeepFactors)
				{
				var delayed = DelayedEntryEvaluator.Evaluate (
					dayHours, r.Date, goLong, goShort, entryPrice, dayMinMove, strong, f, DeepMaxDelayHours);

				bool label = false;

				if (delayed.Executed)
					{
					if (delayed.Result == DelayedIntradayResult.TpFirst)
						label = true;
					else if (delayed.Result == DelayedIntradayResult.SlFirst &&
							 baseOutcome.Result == HourlyTradeResult.SlFirst &&
							 delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
							 delayed.SlPct < baseOutcome.SlPct * 0.7)
						label = true;
					}

				var feats = TargetLevelFeatureBuilder.Build (
					r.Date, goLong, strong, dayMinMove, entryPrice, allHours);

				sink.Add (new PullbackContinuationSample
					{
					Label = label,
					Features = feats,
					EntryUtc = r.Date
					});
				}
			}
		}
	}
