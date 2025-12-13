using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Pnl
	{
	public static class DailyBarePnlChecks
		{
		private sealed class PnlStats
			{
			public int Trades { get; init; }
			public double TotalPnlPct { get; init; }
			public double WinRatePct { get; init; }
			public double MaxDrawdownPct { get; init; }
			public double MeanPct { get; init; }
			public double StdPct { get; init; }
			}

		public static void LogDailyBarePnlWithBaselinesAndShuffle (
			IReadOnlyList<BacktestRecord> records,
			DateTime trainUntilUtc,
			TimeZoneInfo nyTz,
			int shuffleRuns = 20 )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));

			// Пустой набор — это не “ошибка данных”, просто нечего печатать.
			if (records.Count == 0)
				{
				Console.WriteLine ("[pnl-bare] records list is empty, nothing to check.");
				return;
				}

			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var ordered = records
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var boundary = new TrainBoundary (trainUntilUtc, nyTz);
			var split = boundary.Split (ordered, r => r.Causal.DateUtc);

			if (split.Excluded.Count > 0)
				{
				var sample = split.Excluded
					.Take (Math.Min (10, split.Excluded.Count))
					.Select (r => r.Causal.DateUtc.ToString ("O"));
				throw new InvalidOperationException (
					$"[pnl-bare] Found excluded days (baseline-exit undefined). " +
					$"count={split.Excluded.Count}. sample=[{string.Join (", ", sample)}].");
				}

			var train = split.Train;
			var oos = split.Oos;

			Console.WriteLine (
				$"[pnl-bare] trainUntil(baseline-exit)={boundary.TrainUntilIsoDate}, " +
				$"totalRecords={records.Count}, train={train.Count}, oos={oos.Count}, excluded={split.Excluded.Count}");

			// === 1) Bare-PnL по реальным предсказаниям модели ===
			var trainModelStats = ComputeModelPnlStats (train);
			var oosModelStats = ComputeModelPnlStats (oos);

			Console.WriteLine ("[pnl-bare:model] TRAIN: " + FormatStats (trainModelStats));
			Console.WriteLine ("[pnl-bare:model] OOS  : " + FormatStats (oosModelStats));

			// === 2) Baseline: always-long ===
			var trainAlwaysLong = ComputeAlwaysLongPnlStats (train);
			var oosAlwaysLong = ComputeAlwaysLongPnlStats (oos);

			Console.WriteLine ("[pnl-bare:always-long] TRAIN: " + FormatStats (trainAlwaysLong));
			Console.WriteLine ("[pnl-bare:always-long] OOS  : " + FormatStats (oosAlwaysLong));

			// === 3) Baseline: oracle по TrueLabel ===
			var trainOracle = ComputeOraclePnlStats (train);
			var oosOracle = ComputeOraclePnlStats (oos);

			Console.WriteLine ("[pnl-bare:oracle] TRAIN: " + FormatStats (trainOracle));
			Console.WriteLine ("[pnl-bare:oracle] OOS  : " + FormatStats (oosOracle));

			// === 4) Shuffle-пробник ===
			if (shuffleRuns > 0)
				{
				RunShuffleProbe (train, oos, trainModelStats, oosModelStats, shuffleRuns);
				}
			}

		#region Model / baselines

		private static PnlStats ComputeModelPnlStats ( IReadOnlyList<BacktestRecord> records )
			{
			var pnl = CollectPnlSeries (records, getDirection: r => GetModelDirection (r));
			return ComputeStats (pnl);
			}

		private static PnlStats ComputeAlwaysLongPnlStats ( IReadOnlyList<BacktestRecord> records )
			{
			var pnl = CollectPnlSeries (records, getDirection: r => HasValidPrices (r) ? 1 : 0);
			return ComputeStats (pnl);
			}

		private static PnlStats ComputeOraclePnlStats ( IReadOnlyList<BacktestRecord> records )
			{
			var pnl = CollectPnlSeries (
				records,
				getDirection: r =>
				{
					if (!HasValidPrices (r)) return 0;

					var y = r.Causal.TrueLabel;
					return y switch
						{
							2 => 1,
							0 => -1,
							_ => 0
							};
				});

			return ComputeStats (pnl);
			}

		#endregion

		#region Shuffle

		private static void RunShuffleProbe (
			IReadOnlyList<BacktestRecord> train,
			IReadOnlyList<BacktestRecord> oos,
			PnlStats trainModelStats,
			PnlStats oosModelStats,
			int shuffleRuns )
			{
			var trainShuffledTotals = new List<double> (shuffleRuns);
			var oosShuffledTotals = new List<double> (shuffleRuns);

			var trainDirs = train.Select (GetModelDirection).ToArray ();
			var oosDirs = oos.Select (GetModelDirection).ToArray ();

			var rnd = new Random (12345);

			for (int i = 0; i < shuffleRuns; i++)
				{
				var trainDirsShuffled = (int[]) trainDirs.Clone ();
				var oosDirsShuffled = (int[]) oosDirs.Clone ();

				ShuffleInPlace (trainDirsShuffled, rnd);
				ShuffleInPlace (oosDirsShuffled, rnd);

				var trainPnlShuffled = CollectPnlSeries (train, ( idx, _ ) => trainDirsShuffled[idx]);
				var oosPnlShuffled = CollectPnlSeries (oos, ( idx, _ ) => oosDirsShuffled[idx]);

				var trainStatsShuffled = ComputeStats (trainPnlShuffled);
				var oosStatsShuffled = ComputeStats (oosPnlShuffled);

				trainShuffledTotals.Add (trainStatsShuffled.TotalPnlPct);
				oosShuffledTotals.Add (oosStatsShuffled.TotalPnlPct);
				}

			if (trainShuffledTotals.Count > 0)
				{
				Console.WriteLine (
					"[pnl-shuffle:model] TRAIN totalPnL: " +
					$"real={trainModelStats.TotalPnlPct:0.00} %, " +
					$"shuffled avg={trainShuffledTotals.Average ():0.00} %, " +
					$"min={trainShuffledTotals.Min ():0.00} %, max={trainShuffledTotals.Max ():0.00} %");
				}

			if (oosShuffledTotals.Count > 0)
				{
				Console.WriteLine (
					"[pnl-shuffle:model] OOS  totalPnL: " +
					$"real={oosModelStats.TotalPnlPct:0.00} %, " +
					$"shuffled avg={oosShuffledTotals.Average ():0.00} %, " +
					$"min={oosShuffledTotals.Min ():0.00} %, max={oosShuffledTotals.Max ():0.00} %");
				}
			}

		private static void ShuffleInPlace<T> ( T[] array, Random rnd )
			{
			for (int i = array.Length - 1; i > 0; i--)
				{
				int j = rnd.Next (i + 1);
				if (j == i) continue;
				(array[i], array[j]) = (array[j], array[i]);
				}
			}

		#endregion

		#region Common helpers

		private static int GetModelDirection ( BacktestRecord r )
			{
			if (!HasValidPrices (r)) return 0;

			var c = r.Causal;

			bool goLong = c.PredLabel == 2 || (c.PredLabel == 1 && c.PredMicroUp);
			bool goShort = c.PredLabel == 0 || (c.PredLabel == 1 && c.PredMicroDown);

			if (goLong && goShort)
				{
				Console.WriteLine (
					$"[pnl-bare] WARNING: both goLong & goShort for {c.DateUtc:O}, PredLabel={c.PredLabel}, microUp={c.PredMicroUp}, microDown={c.PredMicroDown}.");
				return 0;
				}

			if (goLong) return 1;
			if (goShort) return -1;
			return 0;
			}

		private static bool HasValidPrices ( BacktestRecord r )
			{
			var f = r.Forward;
			return f.Entry > 0.0 &&
				   !double.IsNaN (f.Entry) &&
				   !double.IsNaN (f.Close24);
			}

		private static double ComputeSingleTradePnlPct ( double entry, double close, int direction )
			{
			if (direction == 0 || entry <= 0.0) return 0.0;

			double raw = (close - entry) / entry;
			double signed = direction > 0 ? raw : -raw;
			return signed * 100.0;
			}

		private static List<double> CollectPnlSeries (
			IReadOnlyList<BacktestRecord> records,
			Func<BacktestRecord, int> getDirection )
			{
			var res = new List<double> (records.Count);

			foreach (var r in records)
				{
				int dir = getDirection (r);
				if (dir == 0) continue;

				if (!HasValidPrices (r))
					{
					Console.WriteLine (
						$"[pnl-bare] WARNING: invalid prices for {r.Causal.DateUtc:O}, Entry={r.Forward.Entry}, Close24={r.Forward.Close24}, skip day.");
					continue;
					}

				var pnl = ComputeSingleTradePnlPct (r.Forward.Entry, r.Forward.Close24, dir);
				res.Add (pnl);
				}

			return res;
			}

		private static List<double> CollectPnlSeries (
			IReadOnlyList<BacktestRecord> records,
			Func<int, BacktestRecord, int> getDirection )
			{
			var res = new List<double> (records.Count);

			for (int i = 0; i < records.Count; i++)
				{
				var r = records[i];
				int dir = getDirection (i, r);
				if (dir == 0) continue;

				if (!HasValidPrices (r))
					{
					Console.WriteLine (
						$"[pnl-bare] WARNING: invalid prices for {r.Causal.DateUtc:O}, Entry={r.Forward.Entry}, Close24={r.Forward.Close24}, skip day.");
					continue;
					}

				var pnl = ComputeSingleTradePnlPct (r.Forward.Entry, r.Forward.Close24, dir);
				res.Add (pnl);
				}

			return res;
			}

		private static PnlStats ComputeStats ( List<double> pnlSeries )
			{
			if (pnlSeries == null || pnlSeries.Count == 0)
				{
				return new PnlStats
					{
					Trades = 0,
					TotalPnlPct = 0.0,
					WinRatePct = 0.0,
					MaxDrawdownPct = 0.0,
					MeanPct = 0.0,
					StdPct = 0.0
					};
				}

			int n = pnlSeries.Count;
			double total = pnlSeries.Sum ();
			int wins = pnlSeries.Count (p => p > 0.0);
			double winRate = (double) wins / n * 100.0;

			double mean = total / n;

			double variance = 0.0;
			if (n > 1)
				{
				double sumSq = 0.0;
				foreach (var p in pnlSeries)
					{
					double d = p - mean;
					sumSq += d * d;
					}

				variance = sumSq / (n - 1);
				}

			double std = Math.Sqrt (variance);

			double equity = 1.0;
			double peak = 1.0;
			double maxDd = 0.0;

			foreach (var p in pnlSeries)
				{
				equity *= 1.0 + p / 100.0;
				if (equity > peak)
					{
					peak = equity;
					}

				double dd = (equity / peak - 1.0) * 100.0;
				if (dd < maxDd)
					{
					maxDd = dd;
					}
				}

			return new PnlStats
				{
				Trades = n,
				TotalPnlPct = total,
				WinRatePct = winRate,
				MaxDrawdownPct = maxDd,
				MeanPct = mean,
				StdPct = std
				};
			}

		private static string FormatStats ( PnlStats s )
			{
			return
				$"trades={s.Trades}, " +
				$"totalPnL={s.TotalPnlPct:0.00} %, " +
				$"winRate={s.WinRatePct:0.0} %, " +
				$"maxDD={s.MaxDrawdownPct:0.0} %, " +
				$"mean={s.MeanPct:0.00} %, " +
				$"std={s.StdPct:0.00} %";
			}

		#endregion
		}
	}
