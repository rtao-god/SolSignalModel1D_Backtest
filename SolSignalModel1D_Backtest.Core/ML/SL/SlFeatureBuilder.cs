using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Фичи для SL-модели.
	/// ВАЖНО (каузальность):
	/// используем только 1h-свечи, которые ЗАКРЫЛИСЬ на момент entryUtc.
	/// То есть candle.OpenTimeUtc + 1h <= entryUtc.
	/// Это защищает от "мелкой" утечки, когда entryUtc не на границе часа.
	/// </summary>
	public static class SlFeatureBuilder
		{
		private static readonly TimeSpan Tf1h = TimeSpan.FromHours (1);

		public static float[] Build (
			DateTime entryUtc,
			bool goLong,
			bool strongSignal,
			double dayMinMove,
			double entryPrice,
			IReadOnlyList<Candle1h>? candles1h )
			{
			if (entryUtc == default)
				throw new ArgumentException ("entryUtc must be initialized (non-default).", nameof (entryUtc));
			if (entryUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("entryUtc must be UTC (DateTimeKind.Utc).", nameof (entryUtc));
			if (entryPrice <= 0)
				throw new ArgumentOutOfRangeException (nameof (entryPrice), entryPrice, "entryPrice must be > 0.");

			if (candles1h == null || candles1h.Count == 0)
				throw new InvalidOperationException ("[sl-feats] candles1h is null/empty: SL features require 1h history.");

			var feats = new float[MlSchema.FeatureCount];

			// 0-2: базовая инфа по сигналу
			feats[0] = goLong ? 1f : 0f;
			feats[1] = strongSignal ? 1f : 0f;
			feats[2] = (float) dayMinMove;

			// Берём последние 6 часов ДО входа, но только "закрытые" свечи.
			// Если entryUtc не на границе часа (например 06:30), свеча 06:00–07:00 НЕ должна попадать.
			var knownUntilOpenUtc = entryUtc - Tf1h;               // последний часовой бар, который точно закрыт на entryUtc
			var windowFromUtc = entryUtc.AddHours (-6);            // нижняя граница окна по open-time

			int endExclusive = UpperBoundOpenTimeUtc (candles1h, knownUntilOpenUtc);
			int startInclusive = LowerBoundOpenTimeUtc (candles1h, windowFromUtc);

			if (endExclusive <= startInclusive)
				{
				// Нормальная ситуация на краю истории/при дырках: фичи останутся базовыми.
				// Это лучше, чем тихо использовать незакрытые данные.
				return feats;
				}

			// Копируем только маленький хвост (обычно <= 6 свечей).
			// Не используем LINQ, чтобы не плодить аллокации на каждый сэмпл.
			var lastClosedHours = new List<Candle1h> (Math.Min (endExclusive - startInclusive, 8));
			for (int i = startInclusive; i < endExclusive; i++)
				{
				var c = candles1h[i];

				// Защита от мусора/дублей: порядок должен быть неубывающим.
				// Дубликаты по OpenTimeUtc допускаем только если upstream уже их почистил,
				// иначе это ломает детерминизм фичей.
				if (lastClosedHours.Count > 0 && c.OpenTimeUtc < lastClosedHours[lastClosedHours.Count - 1].OpenTimeUtc)
					{
					throw new InvalidOperationException (
						$"[sl-feats] candles1h must be sorted by OpenTimeUtc ascending. " +
						$"Found inversion at idx={i}: {c.OpenTimeUtc:O} < prev {lastClosedHours[lastClosedHours.Count - 1].OpenTimeUtc:O}.");
					}

				// Дополнительная каузальная гарантия:
				// свеча должна быть закрыта на entryUtc.
				if (c.OpenTimeUtc + Tf1h > entryUtc)
					continue;

				lastClosedHours.Add (c);
				}

			if (lastClosedHours.Count == 0)
				return feats;

			// Сгруппируем в 2h-блоки: [h0,h1], [h2,h3], [h4,h5]
			var blocks2h = Build2hBlocks (lastClosedHours);

			// ===== 2h-агрегаты =====
			if (blocks2h.Count > 0)
				{
				double totalHigh = blocks2h[0].High;
				double totalLow = blocks2h[0].Low;

				for (int i = 1; i < blocks2h.Count; i++)
					{
					var b = blocks2h[i];
					if (b.High > totalHigh) totalHigh = b.High;
					if (b.Low < totalLow) totalLow = b.Low;
					}

				feats[3] = (float) ((totalHigh - totalLow) / entryPrice);    // общий 2h range
				feats[6] = (float) ((totalHigh - entryPrice) / entryPrice);  // насколько мы под хай 2h
				feats[7] = (float) ((entryPrice - totalLow) / entryPrice);   // насколько мы над лоу 2h
				}

			// первый 2h-блок (самый “старый” из последних 6h)
			if (blocks2h.Count >= 1)
				{
				var b0 = blocks2h[0];
				feats[4] = (float) ((b0.High - b0.Low) / entryPrice);
				}

			// последний 2h-блок (как выглядел рынок прямо перед входом)
			if (blocks2h.Count >= 2)
				{
				var bLast = blocks2h[blocks2h.Count - 1];
				feats[5] = (float) ((bLast.High - bLast.Low) / entryPrice);
				}

			// ===== 1h-хвост (последняя ЗАКРЫТАЯ 1h свеча перед entry) =====
			var last1h = lastClosedHours[lastClosedHours.Count - 1];
			double lastRange = last1h.High - last1h.Low;
			double lastBody = Math.Abs (last1h.Close - last1h.Open);
			double wickiness = lastRange > 0 ? 1.0 - (lastBody / lastRange) : 0.0;
			feats[8] = (float) wickiness;

			// час дня (UTC)
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

		/// <summary>
		/// LowerBound по OpenTimeUtc: первый индекс i, где all[i].OpenTimeUtc >= t.
		/// Требование: список отсортирован по OpenTimeUtc.
		/// </summary>
		private static int LowerBoundOpenTimeUtc ( IReadOnlyList<Candle1h> all, DateTime t )
			{
			int lo = 0;
			int hi = all.Count;

			while (lo < hi)
				{
				int mid = lo + ((hi - lo) >> 1);
				if (all[mid].OpenTimeUtc < t)
					lo = mid + 1;
				else
					hi = mid;
				}

			return lo;
			}

		/// <summary>
		/// UpperBound по OpenTimeUtc: первый индекс i, где all[i].OpenTimeUtc > t.
		/// </summary>
		private static int UpperBoundOpenTimeUtc ( IReadOnlyList<Candle1h> all, DateTime t )
			{
			int lo = 0;
			int hi = all.Count;

			while (lo < hi)
				{
				int mid = lo + ((hi - lo) >> 1);
				if (all[mid].OpenTimeUtc <= t)
					lo = mid + 1;
				else
					hi = mid;
				}

			return lo;
			}
		}
	}
