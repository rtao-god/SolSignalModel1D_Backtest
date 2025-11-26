using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Diagnostics.SL
	{
	/// <summary>
	/// Диагностика SL-модели (SlFirstTrainer):
	/// строит/использует модель на готовых SlHitSample и считает PFI + direction.
	/// </summary>
	public static class SlModelDiagnostics
		{
		/// <summary>
		/// PFI + direction по SL-модели на заданном наборе SlHitSample.
		/// 
		/// samples — любой срез (train / OOS / holdout), без утечек.
		/// modelOverride — если передан, используем его; иначе тренируем модель сами.
		/// featureNames — имена фич; если null, используем sl_f00..sl_fNN.
		/// </summary>
		public static void LogFeatureImportanceOnSlModel (
			List<SlHitSample> samples,
			string datasetTag,
			ITransformer? modelOverride = null,
			string[]? featureNames = null )
			{
			if (samples == null) throw new ArgumentNullException (nameof (samples));

			if (samples.Count < 20)
				{
				Console.WriteLine ($"[pfi:sl:{datasetTag}] too few samples ({samples.Count}), skip.");
				return;
				}

			var minDate = samples.Min (s => s.EntryUtc);
			var maxDate = samples.Max (s => s.EntryUtc);
			int pos = samples.Count (s => s.Label);
			int neg = samples.Count - pos;

			Console.WriteLine (
				$"[pfi:sl:{datasetTag}] samples={samples.Count}, pos={pos}, neg={neg}, " +
				$"period={minDate:yyyy-MM-dd}..{maxDate:yyyy-MM-dd}");

			ITransformer model;
			if (modelOverride != null)
				{
				model = modelOverride;
				}
			else
				{
				var trainer = new SlFirstTrainer ();
				var asOf = maxDate;
				model = trainer.Train (samples, asOf);
				}

			// Отдельный MLContext под диагностику (не лезем в рантайм-контекст).
			var ml = new MLContext (seed: 42);

			// Для PFI достаточно Label + Features.
			var data = ml.Data.LoadFromEnumerable (
				samples.Select (s => new MlSampleBinary
					{
					Label = s.Label,
					Features = s.Features
					})
			);

			var names = featureNames
				?? Enumerable
					.Range (0, MlSchema.FeatureCount)
					.Select (i => $"sl_f{i:00}")
					.ToArray ();

			FeatureImportanceAnalyzer.LogBinaryFeatureImportance (
				ml,
				model,
				data,
				names,
				tag: datasetTag);
			}
		}
	}
