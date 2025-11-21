using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Diagnostics.Daily
	{
	/// <summary>
	/// Диагностика дневных моделей (move / dir-normal / dir-down / micro-flat):
	/// считает PFI + direction на произвольном срезе DataRow (train / OOS / holdout)
	/// и печатает таблички в консоль.
	/// </summary>
	public static class DailyModelDiagnostics
		{
		/// <summary>
		/// PFI + direction по всем дневным моделям на заданном наборе DataRow.
		/// Ничего не обучает, кроме использования уже готового ModelBundle.
		/// </summary>
		public static void LogFeatureImportanceOnDailyModels (
			ModelBundle bundle,
			IEnumerable<DataRow> evalRows,
			string datasetTag = "oos" )
			{
			if (bundle == null) throw new ArgumentNullException (nameof (bundle));
			if (evalRows == null) throw new ArgumentNullException (nameof (evalRows));

			var rows = evalRows.ToList ();
			if (rows.Count == 0)
				{
				Console.WriteLine ($"[pfi:daily:{datasetTag}] empty dataset, nothing to analyze.");
				return;
				}

			var minDate = rows.Min (r => r.Date);
			var maxDate = rows.Max (r => r.Date);
			Console.WriteLine ($"[pfi:daily:{datasetTag}] rows={rows.Count}, period={minDate:yyyy-MM-dd}..{maxDate:yyyy-MM-dd}");

			// Для PFI на eval-сете балансировку отключаем,
			// чтобы не искажать реальное распределение.
			DailyTrainingDataBuilder.Build (
				trainRows: rows,
				balanceMove: false,
				balanceDir: false,
				balanceTargetFrac: 0.5,
				moveTrainRows: out var moveRows,
				dirNormalRows: out var dirNormalRows,
				dirDownRows: out var dirDownRows);

			// MLContext берём из бандла, а если там null (на всякий случай) — создаём свой.
			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			// ===== PFI: move-модель =====
			if (bundle.MoveModel != null && moveRows.Count > 0)
				{
				var moveData = ml.Data.LoadFromEnumerable (
					moveRows.Select (r => new MlSampleBinary
						{
						// Позитив: день НЕ flat (Label != 1)
						Label = r.Label != 1,
						Features = MlTrainingUtils.ToFloatFixed (r.Features)
						})
				);

				FeatureImportanceAnalyzer.LogBinaryFeatureImportance (
					ml,
					bundle.MoveModel,
					moveData,
					DailyFeatureSchema.Names,
					tag: $"{datasetTag}:move");
				}
			else
				{
				Console.WriteLine ($"[pfi:daily:{datasetTag}] move-model or data is empty, skip.");
				}

			// ===== PFI: dir-normal =====
			if (bundle.DirModelNormal != null && dirNormalRows.Count > 0)
				{
				var dirNormalData = ml.Data.LoadFromEnumerable (
					dirNormalRows.Select (r => new MlSampleBinary
						{
						// Позитив: up (Label=2), негатив: down (Label=0)
						Label = r.Label == 2,
						Features = MlTrainingUtils.ToFloatFixed (r.Features)
						})
				);

				FeatureImportanceAnalyzer.LogBinaryFeatureImportance (
					ml,
					bundle.DirModelNormal,
					dirNormalData,
					DailyFeatureSchema.Names,
					tag: $"{datasetTag}:dir-normal");
				}
			else
				{
				Console.WriteLine ($"[pfi:daily:{datasetTag}] dir-normal: no model or no eval-rows, skip.");
				}

			// ===== PFI: dir-down =====
			if (bundle.DirModelDown != null && dirDownRows.Count > 0)
				{
				var dirDownData = ml.Data.LoadFromEnumerable (
					dirDownRows.Select (r => new MlSampleBinary
						{
						Label = r.Label == 2,
						Features = MlTrainingUtils.ToFloatFixed (r.Features)
						})
				);

				FeatureImportanceAnalyzer.LogBinaryFeatureImportance (
					ml,
					bundle.DirModelDown,
					dirDownData,
					DailyFeatureSchema.Names,
					tag: $"{datasetTag}:dir-down");
				}
			else
				{
				Console.WriteLine ($"[pfi:daily:{datasetTag}] dir-down: no model or no eval-rows, skip.");
				}

			// ===== PFI: micro-flat =====
			if (bundle.MicroFlatModel != null)
				{
				var microRows = rows
					.Where (r => r.FactMicroUp || r.FactMicroDown)
					.OrderBy (r => r.Date)
					.ToList ();

				if (microRows.Count >= 10)
					{
					var microData = ml.Data.LoadFromEnumerable (
						microRows.Select (r => new MlSampleBinary
							{
							// Позитив: microUp, негатив: microDown
							Label = r.FactMicroUp,
							Features = MlTrainingUtils.ToFloatFixed (r.Features)
							})
					);

					FeatureImportanceAnalyzer.LogBinaryFeatureImportance (
						ml,
						bundle.MicroFlatModel,
						microData,
						MicroFeatureSchema.Names,
						tag: $"{datasetTag}:micro-flat");
					}
				else
					{
					Console.WriteLine ($"[pfi:daily:{datasetTag}] micro: too few micro-rows ({microRows.Count}), skip.");
					}
				}
			else
				{
				Console.WriteLine ($"[pfi:daily:{datasetTag}] MicroFlatModel == null, skip micro layer PFI.");
				}
			}
		}
	}
