using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Оффлайн-строитель для сильного отката (Model A).
	/// Строит сэмплы только на по-настоящему плохих днях (base=SL-first).
	/// </summary>
	public static class PullbackContinuationOfflineBuilder
		{
		// несколько глубин, чтобы раздуть датасет
		private static readonly double[] DeepFactors = new[] { 0.35, 0.45, 0.55 };
		private const double DeepMaxDelayHours = 4.0;

		public static List<PullbackContinuationSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var res = new List<PullbackContinuationSample> (rows.Count * 4);

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

				// гипотетический long и шорт — чтобы совпасть с онлайном
				BuildForDir (res, r, dayHours, entry, minMove, goLong: true);
				BuildForDir (res, r, dayHours, entry, minMove, goLong: false);
				}

			return res;
			}

		private static void BuildForDir (
			List<PullbackContinuationSample> sink,
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

			// нас интересуют только реально плохие дни
			if (baseOutcome.Result != HourlyTradeResult.SlFirst)
				return;

			foreach (var f in DeepFactors)
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
					DeepMaxDelayHours
				);

				bool label = false;

				if (delayed.Executed)
					{
					// кейс 1: 12:00 был SL/None, delayed дал TP
					if (delayed.Result == DelayedIntradayResult.TpFirst)
						{
						label = true;
						}
					else if (delayed.Result == DelayedIntradayResult.SlFirst &&
							 baseOutcome.Result == HourlyTradeResult.SlFirst &&
							 delayed.SlPct > 0 &&
							 baseOutcome.SlPct > 0 &&
							 delayed.SlPct < baseOutcome.SlPct * 0.7)
						{
						// кейс 2: оба SL, но отложка явно лучше
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
