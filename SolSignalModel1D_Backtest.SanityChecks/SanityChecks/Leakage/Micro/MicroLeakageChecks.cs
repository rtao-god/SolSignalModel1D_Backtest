using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Micro
	{
	/// <summary>
	/// Sanity-проверки для микро-слоя:
	/// - достаточное число размеченных микро-дней;
	/// - покрытие прогнозами;
	/// - сравнение accuracies train vs OOS и с рандомным baseline.
	/// </summary>
	public static class MicroLeakageChecks
		{
		/// <summary>
		/// Основная проверка микро-слоя по текущему контексту.
		/// </summary>
		public static SelfCheckResult CheckMicroLayer ( SelfCheckContext ctx )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			var mornings = ctx.Mornings ?? Array.Empty<LabeledCausalRow> ();
			var records = ctx.Records ?? Array.Empty<BacktestRecord> ();

			if (mornings.Count == 0 || records.Count == 0)
				{
				return SelfCheckResult.Ok ("[micro] нет данных для микро-слоя (mornings/records пусты).");
				}

			// Берём утренние точки с path-based микро-разметкой.
			var labeled = mornings
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderBy (r => CausalTimeKey.DayKeyUtc (r))
				.ToList ();

			if (labeled.Count < 20)
				{
				return SelfCheckResult.Ok (
					$"[micro] размеченных микро-дней слишком мало ({labeled.Count}), пропускаем sanity-проверку.");
				}

			// Мапа day-key → факт (FactMicroUp/Down).
			var factByDayKey = labeled.ToDictionary (r => CausalTimeKey.DayKeyUtc (r), r => r);

			// Собираем пары (прогноз микро-слоя + факт) по day-key, чтобы не смешивать timestamp и date-key.
			var pairs = new List<(DateTime DayKeyUtc, bool PredUp, bool FactUp)> ();

			foreach (var rec in records)
				{
				var dayKey = CausalTimeKey.DayKeyUtc (rec);

				if (!factByDayKey.TryGetValue (dayKey, out var row))
					continue;

				// Если микро-прогноз не был выдан, пропускаем день.
				if (!rec.PredMicroUp && !rec.PredMicroDown)
					continue;

				pairs.Add ((
					DayKeyUtc: dayKey,
					PredUp: rec.PredMicroUp,
					FactUp: row.FactMicroUp));
				}

			if (pairs.Count < 20)
				{
				return SelfCheckResult.Ok (
					$"[micro] слишком мало дней, где есть и микро-прогноз, и path-based разметка ({pairs.Count}).");
				}

			// Train/OOS режем по day-key, чтобы сравнение не зависело от внутридневного времени.
			var trainUntilDayKey = ctx.TrainUntilUtc.ToCausalDateUtc ();

			var train = pairs.Where (p => p.DayKeyUtc <= trainUntilDayKey).ToList ();
			var oos = pairs.Where (p => p.DayKeyUtc > trainUntilDayKey).ToList ();

			double accAll = ComputeAccuracy (pairs);
			double accTrain = ComputeAccuracy (train);
			double accOos = ComputeAccuracy (oos);

			const double shuffleAcc = 0.5; // бинарный рандом baseline

			var warnings = new List<string> ();
			var errors = new List<string> ();

			if (train.Count < 30)
				{
				warnings.Add ($"[micro] train-выборка микро-слоя мала ({train.Count}), статистика шумная.");
				}

			if (oos.Count == 0)
				{
				warnings.Add ("[micro] OOS-часть для микро-слоя пуста (нет дней с day-key > trainUntil).");
				}

			// OOS слишком хорош → подозрение на утечку.
			if (!double.IsNaN (accOos) && oos.Count >= 50 && accOos > 0.90)
				{
				errors.Add ($"[micro] OOS accuracy {accOos:P1} при {oos.Count} дней — подозрение на утечку в микро-слое.");
				}

			// Модель почти не лучше рандома по всей выборке.
			if (!double.IsNaN (accAll) && accAll < shuffleAcc + 0.05)
				{
				warnings.Add ($"[micro] accuracy по всей выборке {accAll:P1} почти не лучше случайной модели {shuffleAcc:P1}.");
				}

			// Дегенеративное поведение: почти всегда один знак.
			int predUpCount = pairs.Count (p => p.PredUp);
			int predDownCount = pairs.Count - predUpCount;

			if (predUpCount == 0 || predDownCount == 0)
				{
				warnings.Add ("[micro] микро-слой почти всегда даёт один и тот же знак (up или down) — проверь пороги и покрытие.");
				}

			string summary =
				$"[micro] pairs={pairs.Count}, train={train.Count}, oos={oos.Count}, " +
				$"acc_all={accAll:P1}, acc_train={accTrain:P1}, acc_oos={accOos:P1}";

			var result = new SelfCheckResult
				{
				Success = errors.Count == 0,
				Summary = summary
				};
			result.Errors.AddRange (errors);
			result.Warnings.AddRange (warnings);
			return result;
			}

		private static double ComputeAccuracy ( IReadOnlyList<(DateTime DayKeyUtc, bool PredUp, bool FactUp)> items )
			{
			if (items == null || items.Count == 0)
				return double.NaN;

			int ok = 0;
			for (int i = 0; i < items.Count; i++)
				{
				if (items[i].PredUp == items[i].FactUp)
					ok++;
				}

			return ok / (double) items.Count;
			}
		}
	}
