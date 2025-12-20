using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	public static class SlOfflineBuilder
		{
		public static List<SlHitSample> Build (
			IReadOnlyList<BacktestRecord> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			IReadOnlyDictionary<DateTime, Candle6h> sol6hDict,
			double tpPct = 0.03,
			double slPct = 0.05,
			Func<BacktestRecord, bool>? strongSelector = null )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1m is null/empty: cannot build path-based SL labels.");
			if (sol1h == null || sol1h.Count == 0)
				throw new InvalidOperationException ("[sl-offline] sol1h is null/empty: cannot build SL features.");

			SeriesGuards.EnsureStrictlyAscendingUtc (rows, r => r.Forward.DateUtc, "sl-offline.rows");
			SeriesGuards.EnsureStrictlyAscendingUtc (sol1m, c => c.OpenTimeUtc, "sl-offline.sol1m");
			SeriesGuards.EnsureStrictlyAscendingUtc (sol1h, c => c.OpenTimeUtc, "sl-offline.sol1h");

			if (tpPct < 0) throw new ArgumentOutOfRangeException (nameof (tpPct), tpPct, "tpPct must be >= 0.");
			if (slPct < 0) throw new ArgumentOutOfRangeException (nameof (slPct), slPct, "slPct must be >= 0.");
			if (tpPct <= 0 && slPct <= 0)
				throw new ArgumentOutOfRangeException (
					$"[sl-offline] Invalid config: tpPct<=0 and slPct<=0 (tp={tpPct}, sl={slPct}).");

			var result = new List<SlHitSample> (rows.Count * 2);
			bool hasAnyMorning = false;

			foreach (var r in rows)
				{
				DateTime entryUtc = r.Forward.DateUtc;

				if (!Windowing.IsNyMorning (entryUtc, nyTz: TimeZones.NewYork))
					continue;

				hasAnyMorning = true;

				if (!sol6hDict.TryGetValue (entryUtc, out var c6))
					{
					throw new InvalidOperationException (
						$"[sl-offline] 6h candle not found for morning entry {entryUtc:O}. " +
						"Проверь согласование OpenTimeUtc 6h и Forward.DateUtc.");
					}

				double entry = c6.Close;
				if (entry <= 0)
					{
					throw new InvalidOperationException (
						$"[sl-offline] Non-positive entry price from 6h close for {entryUtc:O}: entry={entry}.");
					}

				double dayMinMove = r.MinMove;
				if (dayMinMove <= 0.0 || double.IsNaN (dayMinMove) || double.IsInfinity (dayMinMove))
					{
					throw new InvalidOperationException (
						$"[sl-offline] Invalid dayMinMove (MinMove) for {entryUtc:O}: {dayMinMove}.");
					}

				bool strongSignal = strongSelector?.Invoke (r) ?? true;

				using var _ = Infra.Causality.CausalityGuard.Begin ("SlOfflineBuilder.Build(morning)", entryUtc);

				DateTime exitUtcExclusive;
				try
					{
					exitUtcExclusive = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz: TimeZones.NewYork);
					}
				catch (Exception ex)
					{
					throw new InvalidOperationException (
						$"[sl-offline] Failed to compute baseline-exit for entry {entryUtc:O}.",
						ex);
					}

				if (exitUtcExclusive <= entryUtc)
					{
					throw new InvalidOperationException (
						$"[sl-offline] Invalid baseline window: exitUtcExclusive <= entryUtc for entry {entryUtc:O}, exit={exitUtcExclusive:O}.");
					}

				int startIdx = LowerBoundOpenTimeUtc (sol1m, entryUtc);
				int endIdxExclusive = LowerBoundOpenTimeUtc (sol1m, exitUtcExclusive);

				if (endIdxExclusive <= startIdx)
					{
					throw new InvalidOperationException (
						$"[sl-offline] No 1m candles in baseline window for entry {entryUtc:O}, exitEx={exitUtcExclusive:O}. " +
						$"Computed range=[{startIdx}; {endIdxExclusive}).");
					}

				// LONG
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

						if (feats.Length != SlSchema.FeatureCount)
							{
							throw new InvalidOperationException (
								$"[sl-offline] SlFeatureBuilder returned invalid length: got={feats.Length}, expected={SlSchema.FeatureCount}.");
							}

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = feats,
							EntryUtc = entryUtc
							});
						}
					}

				// SHORT
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

						if (feats.Length != SlSchema.FeatureCount)
							{
							throw new InvalidOperationException (
								$"[sl-offline] SlFeatureBuilder returned invalid length: got={feats.Length}, expected={SlSchema.FeatureCount}.");
							}

						result.Add (new SlHitSample
							{
							Label = labelRes == HourlyTradeResult.SlFirst,
							Features = feats,
							EntryUtc = entryUtc
							});
						}
					}
				}

			if (!hasAnyMorning)
				return result;

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

			if (entry <= 0)
				throw new InvalidOperationException ($"[sl-offline] entry must be > 0, got {entry}.");

			if (tpPct < 0 || slPct < 0)
				throw new ArgumentOutOfRangeException ($"[sl-offline] tpPct/slPct must be >=0. got tp={tpPct}, sl={slPct}.");

			if (tpPct <= 0 && slPct <= 0)
				throw new InvalidOperationException ($"[sl-offline] tpPct<=0 and slPct<=0 is invalid for path-eval.");

			if (goLong)
				{
				double tp = tpPct > 0 ? entry * (1.0 + tpPct) : double.NaN;
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
				double tp = tpPct > 0 ? entry * (1.0 - tpPct) : double.NaN;
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
		}
	}
