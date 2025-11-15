using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Общий билдер фич для A/B: дневной контекст + короткий intraday-контекст.
	/// </summary>
	public static class TargetLevelFeatureBuilder
		{
		public static float[] Build (
			DateTime dayUtc,
			bool goLong,
			bool strongSignal,
			double dayMinMove,
			double entryPrice,
			List<Candle1h> dayHours )
			{
			// простые intraday-агрегаты
			double high2h = entryPrice;
			double low2h = entryPrice;
			double range2h = 0.0;

			var first2h = dayHours
				.Where (h => (h.OpenTimeUtc - dayUtc).TotalHours <= 2.0)
				.ToList ();

			if (first2h.Count > 0)
				{
				high2h = first2h.Max (h => h.High);
				low2h = first2h.Min (h => h.Low);
				range2h = (high2h - low2h) / entryPrice;
				}

			double pullbackDir = 0.0;
			if (goLong)
				{
				pullbackDir = (entryPrice - low2h) / entryPrice;
				}
			else
				{
				pullbackDir = (high2h - entryPrice) / entryPrice;
				}

			var feats = new float[MlSchema.FeatureCount];
			int i = 0;

			feats[i++] = goLong ? 1f : 0f;
			feats[i++] = strongSignal ? 1f : 0f;
			feats[i++] = (float) dayMinMove;
			feats[i++] = (float) range2h;
			feats[i++] = (float) pullbackDir;

			// остальное нулями
			return feats;
			}
		}
	}
