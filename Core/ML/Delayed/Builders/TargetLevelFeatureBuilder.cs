using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Builders
	{
	/// <summary>
	/// Общий билдер фич для delayed-входов (и для A, и для B).
	/// Берём 24h 1h-свечей и внутри агрегируем в 2h-блоки,
	/// чтобы день выглядел более гладким.
	/// </summary>
	public static class TargetLevelFeatureBuilder
		{
		public static float[] Build (
			DateTime dayStartUtc,
			bool goLong,
			bool strongSignal,
			double dayMinMove,
			double entryPrice,
			List<Candle1h> dayHours )
			{
			var feats = new float[MlSchema.FeatureCount];

			feats[0] = goLong ? 1f : 0f;
			feats[1] = strongSignal ? 1f : 0f;
			feats[2] = (float) dayMinMove;

			if (dayHours == null || dayHours.Count == 0 || entryPrice <= 0)
				return feats;

			// возьмём первые 8h — это 4 двухчасовых блока
			var first8h = dayHours
				.Where (h => h.OpenTimeUtc >= dayStartUtc && h.OpenTimeUtc < dayStartUtc.AddHours (8))
				.OrderBy (h => h.OpenTimeUtc)
				.ToList ();

			var blocks2h = Build2hBlocks (first8h);

			// 3: диапазон первых 2h
			if (blocks2h.Count >= 1)
				{
				var b0 = blocks2h[0];
				feats[3] = (float) ((b0.High - b0.Low) / entryPrice);
				}

			// 4: диапазон первых 4h (первые 2 блока)
			if (blocks2h.Count >= 2)
				{
				double hi = Math.Max (blocks2h[0].High, blocks2h[1].High);
				double lo = Math.Min (blocks2h[0].Low, blocks2h[1].Low);
				feats[4] = (float) ((hi - lo) / entryPrice);
				}

			// 5: диапазон первых 6h (первые 3 блока)
			if (blocks2h.Count >= 3)
				{
				double hi = Math.Max (Math.Max (blocks2h[0].High, blocks2h[1].High), blocks2h[2].High);
				double lo = Math.Min (Math.Min (blocks2h[0].Low, blocks2h[1].Low), blocks2h[2].Low);
				feats[5] = (float) ((hi - lo) / entryPrice);
				}

			// 6: полный диапазон дня по 1h
			double dayHigh = dayHours.Max (h => h.High);
			double dayLow = dayHours.Min (h => h.Low);
			feats[6] = (float) ((dayHigh - dayLow) / entryPrice);

			// 7-8: насколько быстро ушли против позиции в первые 2h/4h
			if (goLong)
				{
				double min2h = blocks2h.Count >= 1 ? blocks2h[0].Low : entryPrice;
				double min4h = blocks2h.Count >= 2 ? Math.Min (blocks2h[0].Low, blocks2h[1].Low) : min2h;
				feats[7] = (float) ((entryPrice - min2h) / entryPrice);
				feats[8] = (float) ((entryPrice - min4h) / entryPrice);
				}
			else
				{
				double max2h = blocks2h.Count >= 1 ? blocks2h[0].High : entryPrice;
				double max4h = blocks2h.Count >= 2 ? Math.Max (blocks2h[0].High, blocks2h[1].High) : max2h;
				feats[7] = (float) ((max2h - entryPrice) / entryPrice);
				feats[8] = (float) ((max4h - entryPrice) / entryPrice);
				}

			// 9: час дня (нормированный) — иногда утром дипы чаще
			feats[9] = dayStartUtc.Hour / 23f;

			// остальное — нули, как и раньше
			return feats;
			}

		private sealed class Block2h
			{
			public double High { get; set; }
			public double Low { get; set; }
			}

		private static List<Block2h> Build2hBlocks ( List<Candle1h> hours )
			{
			var res = new List<Block2h> ();
			for (int i = 0; i < hours.Count; i += 2)
				{
				var c0 = hours[i];
				var block = new Block2h
					{
					High = c0.High,
					Low = c0.Low
					};

				if (i + 1 < hours.Count)
					{
					var c1 = hours[i + 1];
					if (c1.High > block.High) block.High = c1.High;
					if (c1.Low < block.Low) block.Low = c1.Low;
					}

				res.Add (block);
				}
			return res;
			}
		}
	}



