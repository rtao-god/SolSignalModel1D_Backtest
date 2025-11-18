using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Builders
	{
	/// <summary>
	/// Фичи для delayed-моделей (A/B) на момент входа.
	/// Строго каузально: используем только SOL 1h в окне [entryUtc-6h, entryUtc).
	/// </summary>
	public static class TargetLevelFeatureBuilder
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

			// 0–2: базовая информация по сигналу
			feats[0] = goLong ? 1f : 0f;
			feats[1] = strongSignal ? 1f : 0f;
			feats[2] = (float) dayMinMove;

			if (candles1h == null || candles1h.Count == 0 || entryPrice <= 0)
				return feats;

			// последние 6 часов ДО входа
			DateTime from = entryUtc.AddHours (-6);
			var last6 = candles1h
				.Where (c => c.OpenTimeUtc < entryUtc && c.OpenTimeUtc >= from)
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			if (last6.Count == 0)
				return feats;

			// close "сейчас" = close последнего 1h бара до входа
			var lastBar = last6[last6.Count - 1];
			double closeNow = lastBar.Close;
			if (closeNow <= 0)
				closeNow = entryPrice;

			// безопасный доступ к барам "N часов назад"
			Candle1h GetByOffsetFromEnd ( int offset )
				{
				int idx = last6.Count - 1 - offset;
				if (idx < 0) idx = 0;
				return last6[idx];
				}

			// примерно 1h / 3h / 6h назад
			var c1 = last6.Count >= 2 ? GetByOffsetFromEnd (1) : last6[0];
			var c3 = last6.Count >= 4 ? GetByOffsetFromEnd (3) : last6[0];
			var c6 = GetByOffsetFromEnd (Math.Min (5, last6.Count - 1)); // самый старый бар ~6h назад

			double Ret ( double fromClose )
				{
				if (fromClose <= 0.0 || closeNow <= 0.0) return 0.0;
				return closeNow / fromClose - 1.0;
				}

			double ret1h = last6.Count >= 2 ? Ret (c1.Close) : 0.0;
			double ret3h = last6.Count >= 4 ? Ret (c3.Close) : 0.0;
			double ret6h = Ret (c6.Close);

			feats[3] = (float) ret1h;
			feats[4] = (float) ret3h;
			feats[5] = (float) ret6h;

			// ===== диапазоны 3h / 6h =====
			var last3 = last6.Count <= 3
				? last6
				: last6.Skip (last6.Count - 3).ToList ();

			double high3 = last3.Max (h => h.High);
			double low3 = last3.Min (h => h.Low);
			double high6 = last6.Max (h => h.High);
			double low6 = last6.Min (h => h.Low);

			double range3 = (high3 > 0 && low3 > 0 && closeNow > 0)
				? (high3 - low3) / closeNow
				: 0.0;

			double range6 = (high6 > 0 && low6 > 0 && closeNow > 0)
				? (high6 - low6) / closeNow
				: 0.0;

			feats[6] = (float) range3;
			feats[7] = (float) range6;

			// ===== retrace (up / down) в 3h и 6h окнах =====
			double span3 = Math.Max (high3 - low3, 1e-9);
			double span6 = Math.Max (high6 - low6, 1e-9);

			double retr3Up = (closeNow - low3) / span3;
			double retr3Down = (high3 - closeNow) / span3;
			double retr6Up = (closeNow - low6) / span6;
			double retr6Down = (high6 - closeNow) / span6;

			retr3Up = Math.Clamp (retr3Up, 0.0, 1.0);
			retr3Down = Math.Clamp (retr3Down, 0.0, 1.0);
			retr6Up = Math.Clamp (retr6Up, 0.0, 1.0);
			retr6Down = Math.Clamp (retr6Down, 0.0, 1.0);

			feats[8] = (float) retr3Up;
			feats[9] = (float) retr3Down;
			feats[10] = (float) retr6Up;
			feats[11] = (float) retr6Down;

			// ===== локальная волатильность за 6h (RMS лог-ретурнов) =====
			double sumSq = 0.0;
			int steps = 0;
			for (int i = 1; i < last6.Count; i++)
				{
				double prev = last6[i - 1].Close;
				double cur = last6[i].Close;
				if (prev > 0.0 && cur > 0.0)
					{
					double lr = Math.Log (cur / prev);
					sumSq += lr * lr;
					steps++;
					}
				}

			double vol6h = steps > 0 ? Math.Sqrt (sumSq) : 0.0;
			feats[12] = (float) vol6h;

			// ===== час дня (нормированный) =====
			feats[13] = entryUtc.Hour / 23f;

			return feats;
			}
		}
	}
