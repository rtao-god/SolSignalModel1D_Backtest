using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Фичи для SL-модели: только то, что реально было ДО входа.
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

			// последние 6 часов
			var win6 = candles1h
				.Where (c => c.OpenTimeUtc < entryUtc && c.OpenTimeUtc >= entryUtc.AddHours (-6))
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			// последние 2 часа — чтобы понять, что мы уже у хай/лоу
			var win2 = candles1h
				.Where (c => c.OpenTimeUtc < entryUtc && c.OpenTimeUtc >= entryUtc.AddHours (-2))
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			if (win6.Count > 0)
				{
				var last = win6.Last ();
				double lastRet = last.Open > 0 ? (last.Close - last.Open) / last.Open : 0.0;
				feats[3] = (float) lastRet;

				double high6 = win6.Max (c => c.High);
				double low6 = win6.Min (c => c.Low);
				double range6 = high6 - low6;
				feats[4] = (float) (range6 / entryPrice); // относительный диапазон 6h
				feats[5] = (float) ((high6 - entryPrice) / entryPrice); // насколько близко к хай 6h
				feats[6] = (float) ((entryPrice - low6) / entryPrice);  // насколько близко к лоу 6h

				// 7: “шиповость” последнего часа — если огромный хвост против нас, SL часто первый
				double lastRange = last.High - last.Low;
				double lastBody = Math.Abs (last.Close - last.Open);
				double wickiness = lastRange > 0 ? 1.0 - (lastBody / lastRange) : 0.0;
				feats[7] = (float) wickiness;
				}

			if (win2.Count > 0)
				{
				double high2 = win2.Max (c => c.High);
				double low2 = win2.Min (c => c.Low);

				// 8: ближе ли мы к хайу последних 2h
				feats[8] = (float) ((high2 - entryPrice) / entryPrice);
				// 9: ближе ли мы к лою последних 2h
				feats[9] = (float) ((entryPrice - low2) / entryPrice);
				}

			// 10: час дня (норм)
			feats[10] = entryUtc.Hour / 23f;

			// 11: флаг “день уже волатильный по minMove”
			feats[11] = (float) (dayMinMove > 0.025 ? 1f : 0f);

			return feats;
			}
		}
	}
