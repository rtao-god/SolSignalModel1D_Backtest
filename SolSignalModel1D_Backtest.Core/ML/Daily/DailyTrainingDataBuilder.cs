using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Daily
	{
	/// <summary>
	/// Собирает обучающие выборки для дневной модели:
	/// - moveTrainRows: все дни (и с ходом, и без), с опциональным oversample;
	/// - dirNormalRows / dirDownRows: только дни с фактическим ходом, разложенные по режиму.
	/// ВАЖНО: "есть ход" и направление берутся из тех же фактов, что и Label:
	///   Label: 0 = down, 1 = flat, 2 = up (path-based).
	/// </summary>
	public static class DailyTrainingDataBuilder
		{
		/// <summary>
		/// Собирает и при необходимости балансирует выборки для move/dir-моделей.
		/// Move-цель: "день НЕ flat" (Label != 1).
		/// Dir-цель: up vs down по Label (2 = up, 0 = down).
		/// </summary>
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

			// Общая сортировка по времени, чтобы всё было каузально.
			var ordered = trainRows
				.OrderBy (r => r.Date)
				.ToList ();

			// ===== 1. Модель "есть ли ход" (move) видит ВСЕ дни =====
			// Позитивный класс: Label != 1 (т.е. path-based не-flat: up/down).
			if (balanceMove)
				{
				moveTrainRows = MlTrainingUtils.OversampleBinary (
					ordered,
					r => r.Label != 1,
					balanceTargetFrac);
				}
			else
				{
				moveTrainRows = ordered;
				}

			// ===== 2. Модели направления (dir) — только дни с фактическим ходом =====
			// "Есть ход" по факту: Label = 0 (down) или 2 (up).
			var moveRows = ordered
				.Where (r => r.Label == 0 || r.Label == 2)
				.OrderBy (r => r.Date)
				.ToList ();

			// Разделяем по режиму (NORMAL / DOWN), как и раньше.
			dirNormalRows = moveRows
				.Where (r => !r.RegimeDown)
				.OrderBy (r => r.Date)
				.ToList ();

			dirDownRows = moveRows
				.Where (r => r.RegimeDown)
				.OrderBy (r => r.Date)
				.ToList ();

			if (balanceDir)
				{
				// Для обоих режимов бинарная цель: "up?" по Label (2 = up, 0 = down).
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
