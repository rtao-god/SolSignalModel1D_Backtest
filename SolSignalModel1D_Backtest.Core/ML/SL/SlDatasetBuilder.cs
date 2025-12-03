using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Датасет для SL-модели:
	/// - MorningRows — утренние дни, попавшие в train-период;
	/// - Samples — SlHitSample (Label + фичи) для гипотетических long/short.
	/// </summary>
	public sealed class SlDataset
		{
		public List<DataRow> MorningRows { get; }
		public List<SlHitSample> Samples { get; }
		public DateTime TrainUntilUtc { get; }

		public SlDataset (
			List<DataRow> morningRows,
			List<SlHitSample> samples,
			DateTime trainUntilUtc )
			{
			MorningRows = morningRows ?? throw new ArgumentNullException (nameof (morningRows));
			Samples = samples ?? throw new ArgumentNullException (nameof (samples));
			TrainUntilUtc = trainUntilUtc;
			}
		}

	/// <summary>
	/// Dataset-builder для SL-слоя.
	/// Важные инварианты:
	/// - в MorningRows входят только дни с Date <= trainUntil;
	/// - для каждого такого дня считаем path-based факт по 1m до baseline-выхода;
	/// - фичи считаются через SlFeatureBuilder по 1h.
	/// </summary>
	public static class SlDatasetBuilder
		{
		public static SlDataset Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict,
			DateTime trainUntil,
			double tpPct,
			double slPct,
			Func<DataRow, bool>? strongSelector = null )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			var mornings = rows
				.Where (r => r.IsMorning && r.Date <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			var result = new List<SlHitSample> (mornings.Count * 2);

			if (mornings.Count == 0)
				{
				return new SlDataset (mornings, result, trainUntil);
				}

			var all1m = sol1m != null
				? sol1m.OrderBy (m => m.OpenTimeUtc).ToList ()
				: new List<Candle1m> ();

			foreach (var r in mornings)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var c6))
					continue;

				double entry = c6.Close;
				if (entry <= 0)
					continue;

				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0)
					dayMinMove = 0.02;

				bool strongSignal = strongSelector?.Invoke (r) ?? true;

				DateTime entryUtc = r.Date;

				DateTime exitUtc;
				try
					{
					// Используем общий Windowing для baseline-горизонта.
					exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc);
					}
				catch
					{
					// Если baseline-выход не удаётся посчитать (например, из-за gap’ов),
					// просто пропускаем этот день.
					continue;
					}

				if (exitUtc <= entryUtc)
					continue;

				var day1m = all1m
					.Where (m => m.OpenTimeUtc >= entryUtc && m.OpenTimeUtc < exitUtc)
					.ToList ();

				if (day1m.Count == 0)
					continue;

				// --- гипотетический long ---
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
							candles1h: sol1h);

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = entryUtc
							});
						}
					}

				// --- гипотетический short ---
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
							candles1h: sol1h);

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = entryUtc
							});
						}
					}
				}

			return new SlDataset (mornings, result, trainUntil);
			}

		/// <summary>
		/// Path-based факт по 1m: кто был первым — TP или SL.
		/// Логика строго симметрична PnL.
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
