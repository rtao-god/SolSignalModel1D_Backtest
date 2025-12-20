using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.ML.Delayed;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers
	{
	/// <summary>
	/// Trainer для таргет-слоя (0 / 1 / 2).
	/// ВАЖНО: тренер работает только с TargetLevelSample.Features (float[]),
	/// никаких ссылок на Record/Causal/Forward — это убирает “скрытые” зависимости и ошибки типов.
	/// </summary>
	public sealed class TargetLevelTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		private sealed class TrainRow
			{
			public int Label { get; set; }

			[Microsoft.ML.Data.VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Weight { get; set; }
			}

		public ITransformer Train ( List<TargetLevelSample> samples, DateTime asOfUtc )
			{
			if (samples == null) throw new ArgumentNullException (nameof (samples));

			var trainRows = new List<TrainRow> (Math.Max (16, samples.Count));

			for (int i = 0; i < samples.Count; i++)
				{
				var s = samples[i];
				if (s.EntryUtc >= asOfUtc)
					continue;

				if (s.Features == null || s.Features.Length != MlSchema.FeatureCount)
					{
					throw new InvalidOperationException (
						$"[target] sample.Features invalid: expected len={MlSchema.FeatureCount}, actual={s.Features?.Length ?? 0}. " +
						"Фичи должны быть собраны оффлайн-builder’ом строго под схему.");
					}

				// Веса по классу: подчёркиваем редкий “глубокий” класс.
				float w = s.Label switch
					{
						2 => 3.0f,
						1 => 2.0f,
						_ => 1.0f
						};

				// Копирование можно убрать ради скорости/памяти, но оставляем как “идеальный” вариант против мутаций извне.
				var feats = new float[MlSchema.FeatureCount];
				Array.Copy (s.Features, feats, MlSchema.FeatureCount);

				trainRows.Add (new TrainRow
					{
					Label = s.Label,
					Features = feats,
					Weight = w
					});
				}

			if (trainRows.Count == 0)
				throw new InvalidOperationException ("[target] no samples to train.");

			var data = _ml.Data.LoadFromEnumerable (trainRows);

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
				.Append (_ml.Transforms.Conversion.MapKeyToValue (
					outputColumnName: "PredictedLabel",
					inputColumnName: "PredictedLabel"));

			var model = pipeline.Fit (data);
			Console.WriteLine ($"[target] trained on {trainRows.Count} rows (asOf={asOfUtc:yyyy-MM-dd})");
			return model;
			}

		public PredictionEngine<TargetLevelSample, TargetLevelPrediction> CreateEngine ( ITransformer model )
			{
			if (model == null) throw new ArgumentNullException (nameof (model));
			return _ml.Model.CreatePredictionEngine<TargetLevelSample, TargetLevelPrediction> (model);
			}
		}

	public sealed class TargetLevelPrediction
		{
		public int PredictedLabel { get; set; }
		public float[]? Score { get; set; }
		}
	}
