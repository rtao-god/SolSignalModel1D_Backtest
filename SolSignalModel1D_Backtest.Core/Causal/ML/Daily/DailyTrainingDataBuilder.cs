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
			List<DataRow> trainRows,
			bool balanceMove,
			bool balanceDir,
			double balanceTargetFrac,
			out List<DataRow> moveTrainRows,
			out List<DataRow> dirNormalRows,
			out List<DataRow> dirDownRows )
			{
			if (trainRows == null) throw new ArgumentNullException (nameof (trainRows));
			if (trainRows.Count == 0)
				throw new InvalidOperationException ("[daily-train] trainRows is empty.");

			// Контракт: trainRows уже отсортирован по Date и в UTC.
			SeriesGuards.EnsureStrictlyAscendingUtc (trainRows, r => r.Date, "daily-train.trainRows");

			// ===== 1. Move-датасет: все дни =====
			if (balanceMove)
				{
				moveTrainRows = MlTrainingUtils.OversampleBinary (
					trainRows,
					r => r.Label != 1,
					balanceTargetFrac);
				}
			else
				{
				// Без копии: дальше всё равно будет "freeze" в DailyDatasetBuilder.
				moveTrainRows = trainRows;
				}

			// ===== 2. Dir-датасеты: только дни с фактическим ходом =====
			// Порядок сохраняется фильтрацией (trainRows уже отсортирован).
			var moveRows = trainRows
				.Where (r => r.Label == 0 || r.Label == 2)
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
					dirNormalRows,
					r => r.Label == 2,
					balanceTargetFrac);

				dirDownRows = MlTrainingUtils.OversampleBinary (
					dirDownRows,
					r => r.Label == 2,
					balanceTargetFrac);
				}
			}
		}
	}
