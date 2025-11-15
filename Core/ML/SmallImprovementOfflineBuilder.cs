using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Оффлайн-строитель для мелкого улучшения (Model B).
	/// Строит только на "плохих" днях (base=SL-first).
	/// Label = 1, если отложка исполнилась и не была SL-first.
	/// </summary>
	public static class SmallImprovementOfflineBuilder
		{
		private static readonly double[] ShallowFactors = new[] { 0.12, 0.18 };
		private const double ShallowMaxDelayHours = 2.0;

		public static List<SmallImprovementSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var res = new List<SmallImprovementSample> (rows.Count * 3);

			foreach (var r in rows)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var day6h))
					continue;

				double entry = day6h.Close;
				double minMove = r.MinMove;
				if (minMove <= 0) minMove = 0.02;

				DateTime end = r.Date.AddHours (24);
				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= r.Date && h.OpenTimeUtc < end)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();
				if (dayHours.Count == 0)
					continue;

				BuildForDir (res, r, dayHours, entry, minMove, goLong: true);
				BuildForDir (res, r, dayHours, entry, minMove, goLong: false);
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

			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				dayHours,
				r.Date,
				goLong,
				goShort,
				entryPrice,
				dayMinMove,
				strong
			);

			// как и у A — берём только реально "опасные" дни
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
					// B: было плохо, стало не плохо
					if (delayed.Result != DelayedIntradayResult.SlFirst)
						label = true;
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
		}
	}
