using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Pnl
	{
	/// <summary>
	/// Диагностика PnL по PredictionRecord без участия 1m/SL/Delayed/Anti-D.
	/// Используется только Entry/Close24 и направление сделки.
	/// Внутри есть:
	/// - bare-PnL по дневной модели;
	/// - baseline always-long / oracle по TrueLabel;
	/// - shuffle-пробник (перемешивание направлений по дням).
	/// </summary>
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

		/// <summary>
		/// Основной диагностический метод.
		/// Логирует:
		/// - bare-PnL модели (train/OOS);
		/// - baseline always-long / oracle (train/OOS);
		/// - распределение PnL по shuffle-прогонам.
		/// Никаких assert'ов/эксепшенов — только лог в консоль.
		/// </summary>
		public static void LogDailyBarePnlWithBaselinesAndShuffle (
			IReadOnlyList<PredictionRecord> records,
			DateTime trainUntilUtc,
			int shuffleRuns = 20 )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (records.Count == 0)
				{
				Console.WriteLine ("[pnl-bare] records list is empty, nothing to check.");
				return;
				}

			var train = records
				.Where (r => r.DateUtc <= trainUntilUtc)
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var oos = records
				.Where (r => r.DateUtc > trainUntilUtc)
				.OrderBy (r => r.DateUtc)
				.ToList ();

			Console.WriteLine (
				$"[pnl-bare] trainUntilUtc = {trainUntilUtc:yyyy-MM-dd}, " +
				$"totalRecords = {records.Count}, train = {train.Count}, oos = {oos.Count}");

			// === 1. Bare-PnL по реальным предсказаниям модели ===
			var trainModelStats = ComputeModelPnlStats (train);
			var oosModelStats = ComputeModelPnlStats (oos);

			Console.WriteLine (
				"[pnl-bare:model] TRAIN: " +
				FormatStats (trainModelStats));
			Console.WriteLine (
				"[pnl-bare:model] OOS  : " +
				FormatStats (oosModelStats));

			// === 2. Baseline: always-long ===
			var trainAlwaysLong = ComputeAlwaysLongPnlStats (train);
			var oosAlwaysLong = ComputeAlwaysLongPnlStats (oos);

			Console.WriteLine (
				"[pnl-bare:always-long] TRAIN: " +
				FormatStats (trainAlwaysLong));
			Console.WriteLine (
				"[pnl-bare:always-long] OOS  : " +
				FormatStats (oosAlwaysLong));

			// === 3. Baseline: oracle по TrueLabel ===
			var trainOracle = ComputeOraclePnlStats (train);
			var oosOracle = ComputeOraclePnlStats (oos);

			Console.WriteLine (
				"[pnl-bare:oracle] TRAIN: " +
				FormatStats (trainOracle));
			Console.WriteLine (
				"[pnl-bare:oracle] OOS  : " +
				FormatStats (oosOracle));

			// === 4. Shuffle-пробник: перемешиваем направления по дням ===
			if (shuffleRuns > 0)
				{
				RunShuffleProbe (train, oos, trainModelStats, oosModelStats, shuffleRuns);
				}
			}

		#region Model / baselines

		private static PnlStats ComputeModelPnlStats ( List<PredictionRecord> records )
			{
			var pnl = CollectPnlSeries (
				records,
				// Направление берём из PredLabel + микро-слоя.
				getDirection: r => GetModelDirection (r));

			return ComputeStats (pnl);
			}

		private static PnlStats ComputeAlwaysLongPnlStats ( List<PredictionRecord> records )
			{
			var pnl = CollectPnlSeries (
				records,
				// Всегда long, если цены валидные.
				getDirection: r => HasValidPrices (r) ? 1 : 0);

			return ComputeStats (pnl);
			}

		private static PnlStats ComputeOraclePnlStats ( List<PredictionRecord> records )
			{
			var pnl = CollectPnlSeries (
				records,
				// Oracle: long, если TrueLabel == 2; short, если TrueLabel == 0.
				getDirection: r =>
				{
					if (!HasValidPrices (r)) return 0;

					return r.TrueLabel switch
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

		/// <summary>
		/// Shuffle-пробник: перемешивает направления модели по дням и сравнивает
		/// распределение PnL с реальным PnL модели.
		/// </summary>
		private static void RunShuffleProbe (
			List<PredictionRecord> train,
			List<PredictionRecord> oos,
			PnlStats trainModelStats,
			PnlStats oosModelStats,
			int shuffleRuns )
			{
			var trainShuffledTotals = new List<double> (shuffleRuns);
			var oosShuffledTotals = new List<double> (shuffleRuns);

			// Направления модели для последующего перемешивания.
			var trainDirs = train
				.Select (GetModelDirection)
				.ToArray ();

			var oosDirs = oos
				.Select (GetModelDirection)
				.ToArray ();

			var rnd = new Random (12345);

			for (int i = 0; i < shuffleRuns; i++)
				{
				var trainDirsShuffled = (int[]) trainDirs.Clone ();
				var oosDirsShuffled = (int[]) oosDirs.Clone ();

				ShuffleInPlace (trainDirsShuffled, rnd);
				ShuffleInPlace (oosDirsShuffled, rnd);

				var trainPnlShuffled = CollectPnlSeries (
					train,
					( idx, rec ) => trainDirsShuffled[idx]);

				var oosPnlShuffled = CollectPnlSeries (
					oos,
					( idx, rec ) => oosDirsShuffled[idx]);

				var trainStatsShuffled = ComputeStats (trainPnlShuffled);
				var oosStatsShuffled = ComputeStats (oosPnlShuffled);

				trainShuffledTotals.Add (trainStatsShuffled.TotalPnlPct);
				oosShuffledTotals.Add (oosStatsShuffled.TotalPnlPct);
				}

			if (trainShuffledTotals.Count > 0)
				{
				var trainAvg = trainShuffledTotals.Average ();
				var trainMin = trainShuffledTotals.Min ();
				var trainMax = trainShuffledTotals.Max ();

				Console.WriteLine (
					"[pnl-shuffle:model] TRAIN totalPnL: " +
					$"real={trainModelStats.TotalPnlPct:0.00} %, " +
					$"shuffled avg={trainAvg:0.00} %, " +
					$"min={trainMin:0.00} %, max={trainMax:0.00} %");
				}

			if (oosShuffledTotals.Count > 0)
				{
				var oosAvg = oosShuffledTotals.Average ();
				var oosMin = oosShuffledTotals.Min ();
				var oosMax = oosShuffledTotals.Max ();

				Console.WriteLine (
					"[pnl-shuffle:model] OOS  totalPnL: " +
					$"real={oosModelStats.TotalPnlPct:0.00} %, " +
					$"shuffled avg={oosAvg:0.00} %, " +
					$"min={oosMin:0.00} %, max={oosMax:0.00} %");
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

		#region Общие вспомогательные методы

		/// <summary>
		/// Возвращает направление сделки модели:
		/// +1 — long, -1 — short, 0 — no trade.
		/// Логика совпадает с основной: PredLabel + микро-слой.
		/// </summary>
		private static int GetModelDirection ( PredictionRecord r )
			{
			if (!HasValidPrices (r)) return 0;

			bool goLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
			bool goShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);

			if (goLong && goShort)
				{
				// Некорректная ситуация, аккуратно логируем и не торгуем.
				Console.WriteLine (
					$"[pnl-bare] WARNING: both goLong & goShort for {r.DateUtc:O}, PredLabel={r.PredLabel}, microUp={r.PredMicroUp}, microDown={r.PredMicroDown}.");
				return 0;
				}

			if (goLong) return 1;
			if (goShort) return -1;
			return 0;
			}

		/// <summary>
		/// Проверка корректности цен для PnL.
		/// </summary>
		private static bool HasValidPrices ( PredictionRecord r )
			{
			return r.Entry > 0.0 &&
				   !double.IsNaN (r.Entry) &&
				   !double.IsNaN (r.Close24);
			}

		/// <summary>
		/// Вычисляет PnL в процентах для одной сделки в зависимости от направления.
		/// </summary>
		private static double ComputeSingleTradePnlPct ( double entry, double close, int direction )
			{
			if (direction == 0 || entry <= 0.0) return 0.0;

			double raw = (close - entry) / entry; // для long
			double signed = direction > 0 ? raw : -raw;
			return signed * 100.0;
			}

		/// <summary>
		/// Собирает последовательность PnL для всех торговых дней.
		/// Вариант с доступом только к PredictionRecord.
		/// </summary>
		private static List<double> CollectPnlSeries (
			IReadOnlyList<PredictionRecord> records,
			Func<PredictionRecord, int> getDirection )
			{
			var res = new List<double> (records.Count);

			foreach (var r in records)
				{
				int dir = getDirection (r);
				if (dir == 0) continue;

				if (!HasValidPrices (r))
					{
					Console.WriteLine (
						$"[pnl-bare] WARNING: invalid prices for {r.DateUtc:O}, Entry={r.Entry}, Close24={r.Close24}, skip day.");
					continue;
					}

				var pnl = ComputeSingleTradePnlPct (r.Entry, r.Close24, dir);
				res.Add (pnl);
				}

			return res;
			}

		/// <summary>
		/// Собирает последовательность PnL с учётом индекса записи
		/// (используется для shuffle-пробника, где направление берётся из массива).
		/// </summary>
		private static List<double> CollectPnlSeries (
			IReadOnlyList<PredictionRecord> records,
			Func<int, PredictionRecord, int> getDirection )
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
						$"[pnl-bare] WARNING: invalid prices for {r.DateUtc:O}, Entry={r.Entry}, Close24={r.Close24}, skip day.");
					continue;
					}

				var pnl = ComputeSingleTradePnlPct (r.Entry, r.Close24, dir);
				res.Add (pnl);
				}

			return res;
			}

		/// <summary>
		/// Считает сводные метрики по серии дневных PnL (в %).
		/// </summary>
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

			// Макс. просадка по эквити (в %)
			double equity = 1.0;
			double peak = 1.0;
			double maxDd = 0.0; // отрицательное число

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
