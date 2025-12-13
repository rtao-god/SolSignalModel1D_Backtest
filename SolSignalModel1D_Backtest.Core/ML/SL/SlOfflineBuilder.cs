using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	public static class SlOfflineBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public static List<SlHitSample> Build (
			IReadOnlyList<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			IReadOnlyDictionary<DateTime, Candle6h> sol6hDict,
			double tpPct = 0.03,
			double slPct = 0.05,
			Func<DataRow, bool>? strongSelector = null )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1m is null/empty: cannot build path-based SL labels.");
			if (sol1h == null || sol1h.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1h is null/empty: cannot build SL features.");

			// Контракт: всё уже отсортировано на бутстрапе.
			SeriesGuards.EnsureStrictlyAscendingUtc (rows, r => r.Date, "sl-offline.rows");
			SeriesGuards.EnsureStrictlyAscendingUtc (sol1m, c => c.OpenTimeUtc, "sl-offline.sol1m");
			SeriesGuards.EnsureStrictlyAscendingUtc (sol1h, c => c.OpenTimeUtc, "sl-offline.sol1h");

			var result = new List<SlHitSample> (rows.Count * 2);

			// Filter сохраняет порядок (rows уже отсортирован).
			var mornings = rows
				.Where (r => r.IsMorning)
				.ToList ();

			if (mornings.Count == 0)
				return result;

			foreach (var r in mornings)
				{
				if (!sol6hDict.TryGetValue (r.Date, out var c6))
					{
					throw new InvalidOperationException (
						$"[sl-offline] 6h candle not found for morning entry {r.Date:O}. " +
						"Проверь согласование OpenTimeUtc 6h и DataRow.Date.");
					}

				double entry = c6.Close;
				if (entry <= 0)
					{
					throw new InvalidOperationException (
						$"[sl-offline] Non-positive entry price from 6h close for {r.Date:O}: entry={entry}.");
					}

				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0)
					dayMinMove = 0.02;

				bool strongSignal = strongSelector?.Invoke (r) ?? true;

				DateTime entryUtc = r.Date;

				using var _ = Infra.Causality.CausalityGuard.Begin (
					"SlOfflineBuilder.Build(morning)",
					entryUtc
				);

				DateTime exitUtc;
				try
					{
					exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz: NyTz);
					}
				catch (Exception ex)
					{
					throw new InvalidOperationException (
						$"[sl-offline] Failed to compute baseline-exit for entry {entryUtc:O}.",
						ex);
					}

				if (exitUtc <= entryUtc)
					{
					throw new InvalidOperationException (
						$"[sl-offline] Invalid baseline window: exitUtc <= entryUtc for entry {entryUtc:O}, exit={exitUtc:O}.");
					}

				int startIdx = LowerBoundOpenTimeUtc (sol1m, entryUtc);
				int endIdxExclusive = LowerBoundOpenTimeUtc (sol1m, exitUtc);

				if (endIdxExclusive <= startIdx)
					{
					throw new InvalidOperationException (
						$"[sl-offline] No 1m candles in baseline window for entry {entryUtc:O}, exit={exitUtc:O}. " +
						$"Computed range=[{startIdx}; {endIdxExclusive}).");
					}

					{
					var labelRes = EvalPath1m (
						all1m: sol1m,
						startIdx: startIdx,
						endIdxExclusive: endIdxExclusive,
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
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = Pad (feats),
							EntryUtc = entryUtc
							});
						}
					}

					{
					var labelRes = EvalPath1m (
						all1m: sol1m,
						startIdx: startIdx,
						endIdxExclusive: endIdxExclusive,
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

		private static HourlyTradeResult EvalPath1m (
			IReadOnlyList<Candle1m> all1m,
			int startIdx,
			int endIdxExclusive,
			bool goLong,
			double entry,
			double tpPct,
			double slPct )
			{
			if (all1m == null) throw new ArgumentNullException (nameof (all1m));
			if (startIdx < 0 || endIdxExclusive > all1m.Count || endIdxExclusive <= startIdx)
				throw new ArgumentOutOfRangeException (
					$"Invalid 1m range: [{startIdx}; {endIdxExclusive}) for all1m.Count={all1m.Count}.");

			if (entry <= 0) return HourlyTradeResult.None;
			if (tpPct <= 0 && slPct <= 0) return HourlyTradeResult.None;

			if (goLong)
				{
				double tp = entry * (1.0 + Math.Max (tpPct, 0.0));
				double sl = slPct > 0 ? entry * (1.0 - slPct) : double.NaN;

				for (int i = startIdx; i < endIdxExclusive; i++)
					{
					var m = all1m[i];

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

				for (int i = startIdx; i < endIdxExclusive; i++)
					{
					var m = all1m[i];

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

		private static int LowerBoundOpenTimeUtc ( IReadOnlyList<Candle1m> all1m, DateTime t )
			{
			int lo = 0;
			int hi = all1m.Count;

			while (lo < hi)
				{
				int mid = lo + ((hi - lo) >> 1);
				if (all1m[mid].OpenTimeUtc < t)
					lo = mid + 1;
				else
					hi = mid;
				}

			return lo;
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
