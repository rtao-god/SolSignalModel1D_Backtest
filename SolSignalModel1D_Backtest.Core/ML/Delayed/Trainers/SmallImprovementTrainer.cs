using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers
	{
	/// <summary>
	/// Trainer для Model B (small improve).
	/// </summary>
	public sealed class SmallImprovementTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		private sealed class TrainRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Weight { get; set; }
			}

		public ITransformer Train ( List<SmallImprovementSample> samples, DateTime asOfUtc )
			{
			var rows = new List<TrainRow> ();

			foreach (var s in samples)
				{
				if (s.EntryUtc >= asOfUtc)
					continue;

				double ageDays = (asOfUtc - s.EntryUtc).TotalDays;
				float timeW =
					ageDays <= 90 ? 1.0f :
					ageDays <= 180 ? 0.7f :
					0.4f;

				float clsW = s.Forward.TrueLabel ? 2.0f : 1.0f;

				var feats = new float[MlSchema.FeatureCount];
				Array.Copy (s.Causal.Features, feats, Math.Min (s.Causal.Features.Length, MlSchema.FeatureCount));

				rows.Add (new TrainRow
					{
					Label = s.Forward.TrueLabel,
					Features = feats,
					Weight = timeW * clsW
					});
				}

			if (rows.Count == 0)
				throw new InvalidOperationException ("[B-trainer] no samples to train");

			var data = _ml.Data.LoadFromEnumerable (rows);

			var opts = new LightGbmBinaryTrainer.Options
				{
				LabelColumnName = nameof (TrainRow.Forward.TrueLabel),
				FeatureColumnName = nameof (TrainRow.Causal.Features),
				ExampleWeightColumnName = nameof (TrainRow.Weight),
				NumberOfLeaves = 16,
				NumberOfIterations = 90,
				LearningRate = 0.07,
				MinimumExampleCountPerLeaf = 15,
				Seed = 42,
				NumberOfThreads = 1
				};

			var model = _ml.BinaryClassification.Trainers.LightGbm (opts).Fit (data);
			Console.WriteLine ($"[B-trainer] trained on {rows.Count} rows (asOf={asOfUtc:yyyy-MM-dd})");
			return model;
			}

		public PredictionEngine<SmallImprovementSample, SlHitPrediction> CreateEngine ( ITransformer model )
			{
			return _ml.Model.CreatePredictionEngine<SmallImprovementSample, SlHitPrediction> (model);
			}
		}
	}
