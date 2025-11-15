using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Строит каузальный SL-датасет.
	/// Лейбл: кто был первым по 1m (TP / SL).
	/// Фичи: по 1h (см. SlFeatureBuilder).
	/// </summary>
	public static class SlOfflineBuilder
		{
		public static List<SlHitSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var result = new List<SlHitSample> (rows.Count * 2);

			var mornings = rows
				.Where (r => r.IsMorning)
				.OrderBy (r => r.Date)
				.ToList ();

			if (mornings.Count == 0)
				return result;

			foreach (var r in mornings)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var c6))
					continue;
				double entry = c6.Close;
				if (entry <= 0) continue;

				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0) dayMinMove = 0.02;

				// собираем минутки на день
				var day1m = sol1m != null
					? sol1m.Where (m => m.OpenTimeUtc >= r.Date && m.OpenTimeUtc < r.Date.AddHours (24))
						   .OrderBy (m => m.OpenTimeUtc)
						   .ToList ()
					: new List<Candle1m> ();

				// а) гипотетический лонг
					{
					HourlyTradeResult labelRes;

					if (day1m.Count > 0)
						{
						var outcome = MinuteTradeEvaluator.Evaluate (
							day1m,
							r.Date,
							goLong: true,
							goShort: false,
							entryPrice: entry,
							dayMinMove: dayMinMove,
							strongSignal: true
						);
						labelRes = outcome.Result;
						}
					else if (sol1h != null && sol1h.Count > 0)
						{
						var outcome = HourlyTradeEvaluator.EvaluateOne (
							sol1h,
							r.Date,
							goLong: true,
							goShort: false,
							entryPrice: entry,
							dayMinMove: dayMinMove,
							strongSignal: true
						);
						labelRes = outcome.Result;
						}
					else
						{
						labelRes = HourlyTradeResult.None;
						}

					if (labelRes == HourlyTradeResult.SlFirst || labelRes == HourlyTradeResult.TpFirst)
						{
						var feats = SlFeatureBuilder.Build (
							r.Date,
							goLong: true,
							strongSignal: true,
							dayMinMove: dayMinMove,
							entryPrice: entry,
							candles1h: sol1h
						);

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = r.Date
							});
						}
					}

				// б) гипотетический шорт
					{
					HourlyTradeResult labelRes;

					if (day1m.Count > 0)
						{
						var outcome = MinuteTradeEvaluator.Evaluate (
							day1m,
							r.Date,
							goLong: false,
							goShort: true,
							entryPrice: entry,
							dayMinMove: dayMinMove,
							strongSignal: true
						);
						labelRes = outcome.Result;
						}
					else if (sol1h != null && sol1h.Count > 0)
						{
						var outcome = HourlyTradeEvaluator.EvaluateOne (
							sol1h,
							r.Date,
							goLong: false,
							goShort: true,
							entryPrice: entry,
							dayMinMove: dayMinMove,
							strongSignal: true
						);
						labelRes = outcome.Result;
						}
					else
						{
						labelRes = HourlyTradeResult.None;
						}

					if (labelRes == HourlyTradeResult.SlFirst || labelRes == HourlyTradeResult.TpFirst)
						{
						var feats = SlFeatureBuilder.Build (
							r.Date,
							goLong: false,
							strongSignal: true,
							dayMinMove: dayMinMove,
							entryPrice: entry,
							candles1h: sol1h
						);

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = r.Date
							});
						}
					}
				}

			Console.WriteLine ($"[sl-offline] built {result.Count} SL-samples (1m labels, 1h features)");
			return result;
			}

		private static float[] Pad ( float[] src )
			{
			if (src.Length == MlSchema.FeatureCount)
				return src;

			var arr = new float[MlSchema.FeatureCount];
			Array.Copy (src, arr, Math.Min (src.Length, MlSchema.FeatureCount));
			return arr;
			}
		}
	}
