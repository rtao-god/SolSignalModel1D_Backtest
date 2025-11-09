using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Строит большой каузальный SL-датасет без участия дневной модели.
	/// "Что бы было, если бы я в этот день открылся в лонг/шорт?"
	/// </summary>
	public static class SlOfflineBuilder
		{
		public static List<SlHitSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var result = new List<SlHitSample> (rows.Count * 2);

			if (sol1h == null || sol1h.Count == 0)
				return result;

			// берём только утренние ряды — как твои реальные входы
			var mornings = rows
				.Where (r => r.IsMorning)
				.OrderBy (r => r.Date)
				.ToList ();

			foreach (var r in mornings)
				{
				// цена входа — из 6h
				if (!sol6hDict.TryGetValue (r.Date, out var c6))
					continue;
				double entry = c6.Close;
				if (entry <= 0) continue;

				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0) dayMinMove = 0.02;

				// 1) гипотетический ЛОНГ
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

					if (outcome.Result == HourlyTradeResult.SlFirst ||
						outcome.Result == HourlyTradeResult.TpFirst)
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
							Label = outcome.Result == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = r.Date
							});
						}
					}

				// 2) гипотетический ШОРТ
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

					if (outcome.Result == HourlyTradeResult.SlFirst ||
						outcome.Result == HourlyTradeResult.TpFirst)
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
							Label = outcome.Result == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = r.Date
							});
						}
					}
				}

			Console.WriteLine ($"[sl-offline] built {result.Count} SL-samples from synthetic long/short per day");
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
