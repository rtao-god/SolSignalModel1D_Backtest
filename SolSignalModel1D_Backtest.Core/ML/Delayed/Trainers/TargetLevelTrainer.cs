using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers
	{
	/// <summary>
	/// Trainer для таргет-слоя (0 / 1 / 2) поверх оффлайнов, с каузальным срезом.
	/// Лейбл везде int.
	/// </summary>
	public sealed class TargetLevelTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		private sealed class TargetTrainRow
			{
			// важное: int, как и в TargetLevelSample
			public int Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Weight { get; set; }
			}

		public ITransformer Train ( List<TargetLevelSample> samples, DateTime asOfUtc )
			{
			var trainRows = new List<TargetTrainRow> ();

			foreach (var s in samples)
				{
				// каузальность: не берём будущие дни
				if (s.EntryUtc >= asOfUtc)
					continue;

				float w = s.Label switch
					{
						2 => 3.0f,   // глубокий дип, самый редкий
						1 => 2.0f,   // мелкое улучшение
						_ => 1.0f
						};

				var feats = new float[MlSchema.FeatureCount];
				Array.Copy (s.Features, feats, Math.Min (s.Features.Length, MlSchema.FeatureCount));

				trainRows.Add (new TargetTrainRow
					{
					Label = s.Label,
					Features = feats,
					Weight = w
					});
				}

			if (trainRows.Count == 0)
				throw new InvalidOperationException ("[target] no samples to train.");

			var data = _ml.Data.LoadFromEnumerable (trainRows);

			// 1) int -> key
			var pipeline =
				_ml.Transforms.Conversion.MapValueToKey (
						outputColumnName: "Label",
						inputColumnName: "Label")
				.Append (_ml.MulticlassClassification.Trainers.LightGbm (
					new LightGbmMulticlassTrainer.Options
						{
						LabelColumnName = "Label",
						FeatureColumnName = "Features",
						ExampleWeightColumnName = "Weight",
						NumberOfIterations = 120,
						LearningRate = 0.07,
						NumberOfLeaves = 20,
						MinimumExampleCountPerLeaf = 10,
						Seed = 42,
						NumberOfThreads = 1
						}))
				// 2) key -> int обратно
				.Append (_ml.Transforms.Conversion.MapKeyToValue (
					outputColumnName: "PredictedLabel",
					inputColumnName: "PredictedLabel"));

			var model = pipeline.Fit (data);
			Console.WriteLine ($"[target] trained on {trainRows.Count} rows (asOf={asOfUtc:yyyy-MM-dd})");
			return model;
			}

		public PredictionEngine<TargetLevelSample, TargetLevelPrediction> CreateEngine ( ITransformer model )
			{
			return _ml.Model.CreatePredictionEngine<TargetLevelSample, TargetLevelPrediction> (model);
			}
		}

	/// <summary>
	/// Выход таргет-модели: после MapKeyToValue это будет Int32.
	/// </summary>
	public sealed class TargetLevelPrediction
		{
		public int PredictedLabel { get; set; }
		public float[]? Score { get; set; }
		}
	}
