using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Низкоуровневый builder SL-сэмплов для оффлайн-обучения.
	/// Лейбл: кто был первым по 1m (TP / SL) в baseline-окне
	/// entryUtc → следующее рабочее NY-утро (минус 2 минуты).
	/// Фичи: по 1h (см. SlFeatureBuilder).
	/// Не знает про trainUntil и не занимается future-blind фильтрацией.
	/// </summary>
	public static class SlOfflineBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <param name="rows">Дневные строки (RowBuilder).</param>
		/// <param name="sol1h">Вся 1h-история SOL (для фичей).</param>
		/// <param name="sol1m">Вся 1m-история SOL (для path-based факта).</param>
		/// <param name="sol6hDict">6h-словарь SOL (для entry).</param>
		/// <param name="tpPct">TP в долях (0.03 = 3%).</param>
		/// <param name="slPct">SL в долях (0.05 = 5%).</param>
		/// <param name="strongSelector">
		/// Кастомная логика strong/weak для SL-фич:
		/// если null — считаются сильными все дни.
		/// </param>
		public static List<SlHitSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict,
			double tpPct = 0.03,
			double slPct = 0.05,
			Func<DataRow, bool>? strongSelector = null )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			// Оценка верхней границы: максимум два сэмпла (long/short) на утренний день.
			var result = new List<SlHitSample> (rows.Count * 2);

			// Здесь нет trainUntil: он обрабатывается выше, в SlDatasetBuilder через rowsTrain.
			// SlOfflineBuilder работает только по утренним дням.
			var mornings = rows
				.Where (r => r.IsMorning)
				.OrderBy (r => r.Date)
				.ToList ();

			if (mornings.Count == 0)
				return result;

			// Минутные свечи сортируются один раз; path-логика дальше считает только по подокну.
			var all1m = sol1m != null
				? sol1m.OrderBy (m => m.OpenTimeUtc).ToList ()
				: new List<Candle1m> ();

			foreach (var r in mornings)
				{
				// Entry берётся как close утренней 6h-свечи SOL.
				if (!sol6hDict.TryGetValue (r.Date, out var c6))
					continue;

				double entry = c6.Close;
				if (entry <= 0)
					continue;

				// MinMove идёт в фичи; добавляем мягкий пол, чтобы нули не ломали масштаб.
				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0)
					dayMinMove = 0.02;

				// Селектор strong/weak позволяет ограничивать выборку;
				// по умолчанию все дни считаются сильными.
				bool strongSignal = strongSelector?.Invoke (r) ?? true;

				DateTime entryUtc = r.Date;

				// Базовый горизонт выхода совпадает с RowBuilder и PnL-движком.
				DateTime exitUtc;
				try
					{
					exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz: NyTz);
					}
				catch
					{
					// Если корректно посчитать exit не получается (проблема с датой/календарём),
					// день просто исключается из выборки.
					continue;
					}

				if (exitUtc <= entryUtc)
					continue;

				// 1m-окно на baseline-горизонт [entryUtc; exitUtc).
				var day1m = all1m
					.Where (m => m.OpenTimeUtc >= entryUtc && m.OpenTimeUtc < exitUtc)
					.ToList ();

				if (day1m.Count == 0)
					continue;

				// ---- гипотетический long ----
					{
					var labelRes = EvalPath1m (
						day1m: day1m,
						goLong: true,
						entry: entry,
						tpPct: tpPct,
						slPct: slPct);

					if (labelRes == HourlyTradeResult.SlFirst || labelRes == HourlyTradeResult.TpFirst)
						{
						var feats = SlFeatureBuilder.Build (
							entryUtc: entryUtc,
							goLong: true,
							strongSignal: strongSignal,
							dayMinMove: dayMinMove,
							entryPrice: entry,
							candles1h: sol1h
						);

						result.Add (new SlHitSample
							{
							// true  = сначала был SL
							// false = сначала был TP
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = entryUtc
							});
						}
					}

				// ---- гипотетический short ----
					{
					var labelRes = EvalPath1m (
						day1m: day1m,
						goLong: false,
						entry: entry,
						tpPct: tpPct,
						slPct: slPct);

					if (labelRes == HourlyTradeResult.SlFirst || labelRes == HourlyTradeResult.TpFirst)
						{
						var feats = SlFeatureBuilder.Build (
							entryUtc: entryUtc,
							goLong: false,
							strongSignal: strongSignal,
							dayMinMove: dayMinMove,
							entryPrice: entry,
							candles1h: sol1h
						);

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = entryUtc
							});
						}
					}
				}

			Console.WriteLine (
				$"[sl-offline] built {result.Count} SL-samples (1m path labels, 1h features, tp={tpPct:0.###}, sl={slPct:0.###})");

			return result;
			}

		/// <summary>
		/// Path-based факт по 1m: кто был первым — TP или SL.
		/// Логика симметрична PnL:
		/// - long:  TP = High ≥ entry*(1+TP%), SL = Low ≤ entry*(1−SL%);
		/// - short: TP = Low ≤ entry*(1−TP%), SL = High ≥ entry*(1+SL%).
		/// При одновременном срабатывании TP и SL приоритет у SL (консервативный вариант).
		/// </summary>
		private static HourlyTradeResult EvalPath1m (
			List<Candle1m> day1m,
			bool goLong,
			double entry,
			double tpPct,
			double slPct )
			{
			if (day1m == null || day1m.Count == 0) return HourlyTradeResult.None;
			if (entry <= 0) return HourlyTradeResult.None;
			if (tpPct <= 0 && slPct <= 0) return HourlyTradeResult.None;

			if (goLong)
				{
				double tp = entry * (1.0 + Math.Max (tpPct, 0.0));
				double sl = slPct > 0 ? entry * (1.0 - slPct) : double.NaN;

				foreach (var m in day1m)
					{
					bool hitTp = tpPct > 0 && m.High >= tp;
					bool hitSl = slPct > 0 && m.Low <= sl;

					if (hitTp || hitSl)
						{
						if (hitTp && hitSl)
							return HourlyTradeResult.SlFirst;

						return hitSl ? HourlyTradeResult.SlFirst : HourlyTradeResult.TpFirst;
						}
					}
				}
			else
				{
				double tp = entry * (1.0 - Math.Max (tpPct, 0.0));
				double sl = slPct > 0 ? entry * (1.0 + slPct) : double.NaN;

				foreach (var m in day1m)
					{
					bool hitTp = tpPct > 0 && m.Low <= tp;
					bool hitSl = slPct > 0 && m.High >= sl;

					if (hitTp || hitSl)
						{
						if (hitTp && hitSl)
							return HourlyTradeResult.SlFirst;

						return hitSl ? HourlyTradeResult.SlFirst : HourlyTradeResult.TpFirst;
						}
					}
				}

			return HourlyTradeResult.None;
			}

		private static float[] Pad ( float[] src )
			{
			if (src == null) throw new ArgumentNullException (nameof (src));

			if (src.Length == MlSchema.FeatureCount)
				return src;

			var arr = new float[MlSchema.FeatureCount];
			Array.Copy (src, arr, Math.Min (src.Length, MlSchema.FeatureCount));
			return arr;
			}
		}
	}
