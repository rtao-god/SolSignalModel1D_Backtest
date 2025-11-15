using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Фичи для SL-модели.
	/// Главное изменение: контекст берём сглаженный (2h-блоки за последние 6h),
	/// а “шиповость” оставляем по последнему 1h.
	/// </summary>
	public static class SlFeatureBuilder
		{
		public static float[] Build (
			DateTime entryUtc,
			bool goLong,
			bool strongSignal,
			double dayMinMove,
			double entryPrice,
			IReadOnlyList<Candle1h>? candles1h )
			{
			var feats = new float[MlSchema.FeatureCount];

			// 0-2: базовая инфа по сигналу
			feats[0] = goLong ? 1f : 0f;
			feats[1] = strongSignal ? 1f : 0f;
			feats[2] = (float) dayMinMove;

			if (candles1h == null || candles1h.Count == 0 || entryPrice <= 0)
				return feats;

			// соберём последние 6 часов ДО входа
			var last6h = candles1h
				.Where (c => c.OpenTimeUtc < entryUtc && c.OpenTimeUtc >= entryUtc.AddHours (-6))
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			// и последние 12h тоже можно взять — но для стабильности оставим 6h
			// сгруппируем в 2h-блоки: [h0,h1], [h2,h3], [h4,h5]
			var blocks2h = Build2hBlocks (last6h);

			// ===== 2h-агрегаты =====

			// общий диапазон за последние 6h (по 2h-блокам)
			if (blocks2h.Count > 0)
				{
				double totalHigh = blocks2h.Max (b => b.High);
				double totalLow = blocks2h.Min (b => b.Low);
				feats[3] = (float) ((totalHigh - totalLow) / entryPrice);           // общий 2h range
				feats[6] = (float) ((totalHigh - entryPrice) / entryPrice);         // насколько мы под хай 2h
				feats[7] = (float) ((entryPrice - totalLow) / entryPrice);          // насколько мы над лоу 2h
				}

			// первый 2h-блок (самый “старый” из последних 6h)
			if (blocks2h.Count >= 1)
				{
				var b0 = blocks2h[0];
				feats[4] = (float) ((b0.High - b0.Low) / entryPrice);               // ранний range
				}

			// последний 2h-блок (как выглядел рынок прямо перед входом)
			if (blocks2h.Count >= 2)
				{
				var bLast = blocks2h[blocks2h.Count - 1];
				feats[5] = (float) ((bLast.High - bLast.Low) / entryPrice);         // последний range
				}

			// ===== 1h-хвост =====
			if (last6h.Count > 0)
				{
				var last1h = last6h[last6h.Count - 1];
				double lastRange = last1h.High - last1h.Low;
				double lastBody = Math.Abs (last1h.Close - last1h.Open);
				double wickiness = lastRange > 0 ? 1.0 - (lastBody / lastRange) : 0.0;
				feats[8] = (float) wickiness;
				}

			// час дня
			feats[9] = entryUtc.Hour / 23f;

			// день вообще волатильный?
			feats[10] = (float) (dayMinMove > 0.025 ? 1f : 0f);

			return feats;
			}

		private sealed class Block2h
			{
			public double High { get; set; }
			public double Low { get; set; }
			}

		/// <summary>
		/// Группируем по 2 часа подряд. Порядок сохраняем (от старых к новым).
		/// </summary>
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
