using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers
	{
	/// <summary>
	/// Trainer для Model A (deep pullback).
	/// </summary>
	public sealed class PullbackContinuationTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		private sealed class TrainRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Weight { get; set; }
			public DateTime EntryUtc { get; set; }
			}

		public ITransformer Train ( List<PullbackContinuationSample> samples, DateTime asOfUtc )
			{
			var rows = new List<TrainRow> ();

			foreach (var s in samples)
				{
				if (s.EntryUtc >= asOfUtc)
					continue;

				// приоритет свежему
				double ageDays = (asOfUtc - s.EntryUtc).TotalDays;
				float timeW =
					ageDays <= 90 ? 1.0f :
					ageDays <= 180 ? 0.7f :
					0.4f;

				float clsW = s.Label ? 2.5f : 1.0f;

				var feats = new float[MlSchema.FeatureCount];
				Array.Copy (s.Features, feats, Math.Min (s.Features.Length, MlSchema.FeatureCount));

				rows.Add (new TrainRow
					{
					Label = s.Label,
					Features = feats,
					Weight = timeW * clsW,
					EntryUtc = s.EntryUtc
					});
				}

			if (rows.Count == 0)
				throw new InvalidOperationException ("[A-trainer] no samples to train");

			var data = _ml.Data.LoadFromEnumerable (rows);

			var opts = new LightGbmBinaryTrainer.Options
				{
				LabelColumnName = nameof (TrainRow.Label),
				FeatureColumnName = nameof (TrainRow.Features),
				ExampleWeightColumnName = nameof (TrainRow.Weight),
				NumberOfLeaves = 16,
				NumberOfIterations = 90,
				LearningRate = 0.07,
				MinimumExampleCountPerLeaf = 15,
				Seed = 42,
				NumberOfThreads = 1
				};

			var model = _ml.BinaryClassification.Trainers.LightGbm (opts).Fit (data);
			Console.WriteLine ($"[A-trainer] trained on {rows.Count} rows (asOf={asOfUtc:yyyy-MM-dd})");
			return model;
			}

		public PredictionEngine<PullbackContinuationSample, SlHitPrediction> CreateEngine ( ITransformer model )
			{
			// используем тот же предсказательный тип, что и SL (Label:bool, Probability:float)
			return _ml.Model.CreatePredictionEngine<PullbackContinuationSample, SlHitPrediction> (model);
			}
		}
	}
