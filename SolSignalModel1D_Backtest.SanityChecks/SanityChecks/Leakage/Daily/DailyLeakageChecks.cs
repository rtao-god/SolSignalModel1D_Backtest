using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.SanityChecks;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily
	{
	/// <summary>
	/// Sanity-проверки для дневной модели:
	/// - разбиение Train/OOS строго по TrainBoundary (через baseline-exit);
	/// - сравнение accuracies Train vs OOS;
	/// - сравнение с рандомной "shuffle"-моделью.
	///
	/// Важно: excluded (дни без baseline-exit по контракту) не должны попадать в метрики,
	/// иначе сегменты становятся несогласованными и маскируют boundary leakage.
	/// </summary>
	public static class DailyLeakageChecks
		{
		public static SelfCheckResult CheckDailyTrainVsOosAndShuffle (
			IReadOnlyList<BacktestRecord> records,
			TrainBoundary boundary )
			{
			if (records == null || records.Count == 0)
				{
				return SelfCheckResult.Ok ("[daily] нет BacktestRecord'ов — пропускаем дневные проверки.");
				}
			if (boundary.Equals (default (TrainBoundary)))
				{
				throw new ArgumentException ("[daily] TrainBoundary must be initialized (non-default).", nameof (boundary));
				}

			// Стабильный порядок по entryUtc (каузальная дата).
			var ordered = records
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			// ЕДИНСТВЕННОЕ правило сегментации — в boundary.
			var split = boundary.Split (ordered, r => r.Causal.DateUtc);

			var train = split.Train;
			var oos = split.Oos;
			var excluded = split.Excluded;

			// Eligible = train+oos, excluded не смешиваем в метрики.
			var eligible = new List<BacktestRecord> (train.Count + oos.Count);
			eligible.AddRange (train);
			eligible.AddRange (oos);

			if (eligible.Count == 0)
				{
				// Это сильный сигнал, что entryUtc не соответствует контракту baseline-окна
				// (например, все даты попали на weekend по NY, или сломано окно).
				return SelfCheckResult.Fail (
					$"[daily] eligible=0 (train+oos), excluded={excluded.Count}. " +
					$"Проверь контракт entryUtc и baseline-exit. trainUntil={boundary.TrainUntilIsoDate}");
				}

			var warnings = new List<string> ();
			var errors = new List<string> ();

			if (excluded.Count > 0)
				{
				warnings.Add (
					$"[daily] excluded={excluded.Count} (no baseline-exit by contract). " +
					"Эти дни исключены из метрик; проверь weekend/дыры/несогласованность entryUtc.");
				}

			if (oos.Count == 0)
				{
				warnings.Add (
					$"[daily] OOS-часть пуста (нет дней с exit > {boundary.TrainUntilIsoDate}). Метрики будут train-like.");
				}

			double trainAcc = train.Count > 0 ? ComputeAccuracy (train) : double.NaN;
			double oosAcc = oos.Count > 0 ? ComputeAccuracy (oos) : double.NaN;
			double allAcc = ComputeAccuracy (eligible);

			// Простейшая baseline-модель: рандомный класс из {0,1,2}.
			double shuffleAcc = ComputeShuffleAccuracy (eligible, classesCount: 3, seed: 42);

			string summary =
				$"[daily] eligible={eligible.Count}, excluded={excluded.Count}, " +
				$"train={train.Count}, oos={oos.Count}, trainUntil(exit<=){boundary.TrainUntilIsoDate}, " +
				$"acc_all={allAcc:P1}, acc_train={trainAcc:P1}, acc_oos={oosAcc:P1}, acc_shuffle≈{shuffleAcc:P1}";

			// 1) Слишком высокая точность на train → возможная утечка или экстремальный overfit.
			if (!double.IsNaN (trainAcc) && train.Count >= 200 && trainAcc > 0.95)
				{
				errors.Add ($"[daily] train accuracy {trainAcc:P1} при {train.Count} дней — подозрение на утечку.");
				}

			// 2) Слишком высокая точность на OOS → почти точно утечка.
			if (!double.IsNaN (oosAcc) && oos.Count >= 100 && oosAcc > 0.90)
				{
				errors.Add ($"[daily] OOS accuracy {oosAcc:P1} при {oos.Count} дней — подозрение на утечку.");
				}

			// 3) Модель почти не лучше рандома — сигнал про данные/лейблы/шум (не обяз. утечка).
			if (!double.IsNaN (allAcc) && !double.IsNaN (shuffleAcc) && allAcc < shuffleAcc + 0.05)
				{
				warnings.Add ($"[daily] accuracy по eligible {allAcc:P1} почти не лучше shuffle {shuffleAcc:P1}.");
				}

			var result = new SelfCheckResult
				{
				Success = errors.Count == 0,
				Summary = summary
				};

			result.Errors.AddRange (errors);
			result.Warnings.AddRange (warnings);

			result.Metrics["daily.eligible"] = eligible.Count;
			result.Metrics["daily.excluded"] = excluded.Count;

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
				if (r.Causal.PredLabel == r.Causal.TrueLabel)
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
				if (randomLabel == r.Causal.TrueLabel)
					ok++;
				}

			return ok / (double) records.Count;
			}
		}
	}
