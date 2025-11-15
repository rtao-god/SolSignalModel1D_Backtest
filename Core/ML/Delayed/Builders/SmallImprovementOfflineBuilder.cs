using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Builders;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Оффлайн-строитель для Model B (small improvement).
	/// Строит только на реально проблемных днях (base=SL-first),
	/// но пробует несколько мелких шагов, чтобы раздуть датасет.
	/// </summary>
	public static class SmallImprovementOfflineBuilder
		{
		private static readonly double[] ShallowFactors = new[] { 0.12, 0.18, 0.24 };
		private const double ShallowMaxDelayHours = 2.0;

		public static List<SmallImprovementSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var res = new List<SmallImprovementSample> (rows.Count * 4);

			foreach (var r in rows)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var day6))
					continue;

				double entry = day6.Close;
				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0) dayMinMove = 0.02;

				DateTime end = r.Date.AddHours (24);
				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= r.Date && h.OpenTimeUtc < end)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();
				if (dayHours.Count == 0)
					continue;

				// как и для A — делаем и long и short
				BuildForDir (res, r, dayHours, entry, dayMinMove, goLong: true);
				BuildForDir (res, r, dayHours, entry, dayMinMove, goLong: false);
				}

			return res;
			}

		private static void BuildForDir (
			List<SmallImprovementSample> sink,
			DataRow r,
			List<Candle1h> dayHours,
			double entryPrice,
			double dayMinMove,
			bool goLong )
			{
			bool goShort = !goLong;
			bool strong = true;

			// базовый результат в 12:00
			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				dayHours,
				r.Date,
				goLong,
				goShort,
				entryPrice,
				dayMinMove,
				strong
			);

			// нас интересуют только реально плохие/опасные
			if (baseOutcome.Result != HourlyTradeResult.SlFirst)
				return;

			foreach (var f in ShallowFactors)
				{
				var delayed = DelayedEntryEvaluator.Evaluate (
					dayHours,
					r.Date,
					goLong,
					goShort,
					entryPrice,
					dayMinMove,
					strong,
					f,
					ShallowMaxDelayHours
				);

				bool label = false;

				if (delayed.Executed)
					{
					// цель B: стало НЕ хуже, чем было
					int baseRank = RankHourly (baseOutcome.Result);
					int delayedRank = RankDelayed (delayed.Result);

					if (delayedRank >= baseRank)
						{
						label = true;
						}
					else if (baseOutcome.Result == HourlyTradeResult.SlFirst &&
							 delayed.Result == DelayedIntradayResult.SlFirst &&
							 delayed.SlPct > 0 && baseOutcome.SlPct > 0 &&
							 delayed.SlPct < baseOutcome.SlPct)
						{
						// оба SL, но delayed меньше
						label = true;
						}
					}

				var feats = TargetLevelFeatureBuilder.Build (
					r.Date,
					goLong,
					strong,
					dayMinMove,
					entryPrice,
					dayHours
				);

				sink.Add (new SmallImprovementSample
					{
					Label = label,
					Features = feats,
					EntryUtc = r.Date
					});
				}
			}

		private static int RankHourly ( HourlyTradeResult res )
			{
			return res switch
				{
					HourlyTradeResult.TpFirst => 3,
					HourlyTradeResult.None => 2,
					HourlyTradeResult.Ambiguous => 2,
					HourlyTradeResult.SlFirst => 0,
					_ => 0
					};
			}

		private static int RankDelayed ( DelayedIntradayResult res )
			{
			return res switch
				{
					DelayedIntradayResult.TpFirst => 3,
					DelayedIntradayResult.None => 2,
					DelayedIntradayResult.Ambiguous => 2,
					DelayedIntradayResult.SlFirst => 0,
					_ => 0
					};
			}
		}
	}
