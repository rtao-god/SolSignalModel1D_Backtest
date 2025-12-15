using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Daily
	{
	public static class DailyTrainingDataBuilder
		{
		public static void Build (
			List<BacktestRecord> trainRows,
			bool balanceMove,
			bool balanceDir,
			double balanceTargetFrac,
			out List<BacktestRecord> moveTrainRows,
			out List<BacktestRecord> dirNormalRows,
			out List<BacktestRecord> dirDownRows )
			{
			if (trainRows == null) throw new ArgumentNullException (nameof (trainRows));
			if (trainRows.Count == 0)
				throw new InvalidOperationException ("[daily-train] trainRows is empty.");

			// Контракт: trainRows уже отсортирован по Date и в UTC.
			SeriesGuards.EnsureStrictlyAscendingUtc (trainRows, r => r.ToCausalDateUtc (), "daily-train.trainRows");

			// ===== 1. Move-датасет: все дни =====
			if (balanceMove)
				{
				moveTrainRows = MlTrainingUtils.OversampleBinary (
					src: trainRows,
					isPositive: r => r.Forward.TrueLabel != 1,
					dateSelector: r => r.ToCausalDateUtc (),
					targetFrac: balanceTargetFrac);
				}
			else
				{
				// Без копии: дальше всё равно будет "freeze" в DailyDatasetBuilder.
				moveTrainRows = trainRows;
				}

			// ===== 2. Dir-датасеты: только дни с фактическим ходом =====
			// Порядок сохраняется фильтрацией (trainRows уже отсортирован).
			var moveRows = trainRows
				.Where (r => r.Forward.TrueLabel == 0 || r.Forward.TrueLabel == 2)
				.ToList ();

			dirNormalRows = moveRows
				.Where (r => !r.RegimeDown)
				.ToList ();

			dirDownRows = moveRows
				.Where (r => r.RegimeDown)
				.ToList ();

			if (balanceDir)
				{
				dirNormalRows = MlTrainingUtils.OversampleBinary (
					src: dirNormalRows,
					isPositive: r => r.Forward.TrueLabel == 2,
					dateSelector: r => r.ToCausalDateUtc (),
					targetFrac: balanceTargetFrac);

				dirDownRows = MlTrainingUtils.OversampleBinary (
					src: dirDownRows,
					isPositive: r => r.Forward.TrueLabel == 2,
					dateSelector: r => r.ToCausalDateUtc (),
					targetFrac: balanceTargetFrac);
				}
			}
		}
	}
