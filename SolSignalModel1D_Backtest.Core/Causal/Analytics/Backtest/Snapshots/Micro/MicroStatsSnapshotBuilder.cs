using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Micro
	{
	/// <summary>
	/// Микро-статистика:
	/// 1) Только по предсказанным flat-дням (PredLabel_Day==1) с валидным micro-фактом.
	/// 2) Направленная точность по дням, где и pred, и truth ∈ {0,2} (direction определена).
	/// </summary>
	public static class MicroStatsSnapshotBuilder
		{
		public static MicroStatsSnapshot Build ( IReadOnlyList<BacktestAggRow> rows )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			var flatOnly = BuildFlatOnly (rows);
			var nonFlat = BuildNonFlatDirection (rows);

			return new MicroStatsSnapshot
				{
				FlatOnly = flatOnly,
				NonFlatDirection = nonFlat
				};
			}

		private static FlatOnlyMicroBlock BuildFlatOnly ( IReadOnlyList<BacktestAggRow> rows )
			{
			int microUpPred = 0, microUpHit = 0, microUpMiss = 0;
			int microDownPred = 0, microDownHit = 0, microDownMiss = 0;
			int microNone = 0;

			foreach (var r in rows.Where (x => x.PredLabel_Day == 1))
				{
				// Инварианты взаимной исключаемости должны соблюдаться всегда.
				if (r.PredMicroUp && r.PredMicroDown)
					throw new InvalidOperationException ($"[micro-stats] Both PredMicroUp/PredMicroDown are true for {r.DateUtc:O}.");

				if (r.FactMicroUp && r.FactMicroDown)
					throw new InvalidOperationException ($"[micro-stats] Both FactMicroUp/FactMicroDown are true for {r.DateUtc:O}.");

				// Если у дня нет micro-fact (true flat без наклона) — он не информативен для оценки micro-модели.
				if (!r.FactMicroUp && !r.FactMicroDown)
					continue;

				bool anyPred = false;

				if (r.PredMicroUp)
					{
					anyPred = true;
					microUpPred++;
					if (r.FactMicroUp) microUpHit++; else microUpMiss++;
					}

				if (r.PredMicroDown)
					{
					anyPred = true;
					microDownPred++;
					if (r.FactMicroDown) microDownHit++; else microDownMiss++;
					}

				if (!anyPred)
					microNone++;
				}

			int totalDirPred = microUpPred + microDownPred;
			int totalDirHit = microUpHit + microDownHit;

			double accUp = microUpPred > 0 ? (double) microUpHit / microUpPred * 100.0 : 0.0;
			double accDown = microDownPred > 0 ? (double) microDownHit / microDownPred * 100.0 : 0.0;
			double accAll = totalDirPred > 0 ? (double) totalDirHit / totalDirPred * 100.0 : 0.0;

			return new FlatOnlyMicroBlock
				{
				MicroUpPred = microUpPred,
				MicroUpHit = microUpHit,
				MicroUpMiss = microUpMiss,
				MicroDownPred = microDownPred,
				MicroDownHit = microDownHit,
				MicroDownMiss = microDownMiss,
				MicroNonePredicted = microNone,
				TotalDirPred = totalDirPred,
				TotalDirHit = totalDirHit,
				AccUpPct = accUp,
				AccDownPct = accDown,
				AccAllPct = accAll
				};
			}

		private static NonFlatDirectionBlock BuildNonFlatDirection ( IReadOnlyList<BacktestAggRow> rows )
			{
			// Идеально: direction считается только там, где direction определена и у truth, и у pred.
			var data = rows
				.Where (r => (r.PredLabel_Day == 0 || r.PredLabel_Day == 2) && (r.TrueLabel == 0 || r.TrueLabel == 2))
				.ToList ();

			int total = data.Count;
			int correct = data.Count (r => r.TrueLabel == r.PredLabel_Day);

			int predUp_factUp = data.Count (r => r.PredLabel_Day == 2 && r.TrueLabel == 2);
			int predUp_factDown = data.Count (r => r.PredLabel_Day == 2 && r.TrueLabel == 0);
			int predDown_factDown = data.Count (r => r.PredLabel_Day == 0 && r.TrueLabel == 0);
			int predDown_factUp = data.Count (r => r.PredLabel_Day == 0 && r.TrueLabel == 2);

			double acc = total > 0 ? (double) correct / total * 100.0 : 0.0;

			return new NonFlatDirectionBlock
				{
				Total = total,
				Correct = correct,
				PredUp_FactUp = predUp_factUp,
				PredUp_FactDown = predUp_factDown,
				PredDown_FactDown = predDown_factDown,
				PredDown_FactUp = predDown_factUp,
				AccuracyPct = acc
				};
			}
		}
	}
