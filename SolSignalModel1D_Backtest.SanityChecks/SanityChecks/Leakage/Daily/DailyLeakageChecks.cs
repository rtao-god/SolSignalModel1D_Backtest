using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.SanityChecks;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily
	{
	/// <summary>
	/// Sanity-проверки для дневной модели:
	/// - разделение train / OOS по дате;
	/// - сравнение accuracies train vs OOS;
	/// - сравнение с рандомной "shuffle"-моделью.
	/// </summary>
	public static class DailyLeakageChecks
		{
		public static SelfCheckResult CheckDailyTrainVsOosAndShuffle (
			IReadOnlyList<BacktestRecord> records,
			DateTime trainUntilUtc )
			{
			if (records == null || records.Count == 0)
				{
				return SelfCheckResult.Ok ("[daily] нет PredictionRecord'ов — пропускаем дневные проверки.");
				}

			var ordered = records.OrderBy (r => r.DateUtc).ToList ();
			var train = ordered.Where (r => r.DateUtc <= trainUntilUtc).ToList ();
			var oos = ordered.Where (r => r.DateUtc > trainUntilUtc).ToList ();

			var warnings = new List<string> ();
			var errors = new List<string> ();

			if (oos.Count == 0)
				{
				warnings.Add ("[daily] OOS-часть пуста (нет дней с DateUtc > _trainUntilUtc). Метрики будут train-like.");
				}

			double trainAcc = train.Count > 0 ? ComputeAccuracy (train) : double.NaN;
			double oosAcc = oos.Count > 0 ? ComputeAccuracy (oos) : double.NaN;
			double allAcc = ComputeAccuracy (ordered);

			// Простейшая baseline-модель: рандомный класс из {0,1,2}.
			double shuffleAcc = ComputeShuffleAccuracy (ordered, classesCount: 3, seed: 42);

			string summary =
				$"[daily] records={records.Count}, train={train.Count}, oos={oos.Count}, " +
				$"acc_all={allAcc:P1}, acc_train={trainAcc:P1}, acc_oos={oosAcc:P1}, acc_shuffle≈{shuffleAcc:P1}";

			// 1) Слишком высокая точность на train → возможная утечка или overfit.
			if (!double.IsNaN (trainAcc) && train.Count >= 200 && trainAcc > 0.95)
				{
				errors.Add ($"[daily] train accuracy {trainAcc:P1} при {train.Count} дней — подозрение на утечку.");
				}

			// 2) Слишком высокая точность на OOS → почти точно утечка.
			if (!double.IsNaN (oosAcc) && oos.Count >= 100 && oosAcc > 0.90)
				{
				errors.Add ($"[daily] OOS accuracy {oosAcc:P1} при {oos.Count} дней — подозрение на утечку.");
				}

			// 3) Модель не сильно лучше рандома.
			if (allAcc < shuffleAcc + 0.05)
				{
				warnings.Add ($"[daily] accuracy по всей выборке {allAcc:P1} почти не лучше shuffle {shuffleAcc:P1}.");
				}

			var result = new SelfCheckResult
				{
				Success = errors.Count == 0,
				Summary = summary
				};

			result.Errors.AddRange (errors);
			result.Warnings.AddRange (warnings);

			// Метрики, которые будут использоваться тестами и для логов.
			result.Metrics["daily.acc_all"] = allAcc;
			result.Metrics["daily.acc_train"] = trainAcc;
			result.Metrics["daily.acc_oos"] = oosAcc;
			result.Metrics["daily.acc_shuffle"] = shuffleAcc;

			return result;
			}

		private static double ComputeAccuracy ( IReadOnlyList<BacktestRecord> records )
			{
			if (records.Count == 0) return double.NaN;

			int ok = 0;
			foreach (var r in records)
				{
				if (r.PredLabel == r.TrueLabel)
					ok++;
				}

			return ok / (double) records.Count;
			}

		private static double ComputeShuffleAccuracy (
			IReadOnlyList<BacktestRecord> records,
			int classesCount,
			int seed )
			{
			if (records.Count == 0 || classesCount <= 1) return double.NaN;

			var rnd = new Random (seed);
			int ok = 0;

			foreach (var r in records)
				{
				int randomLabel = rnd.Next (classesCount);
				if (randomLabel == r.TrueLabel)
					ok++;
				}

			return ok / (double) records.Count;
			}
		}
	}
