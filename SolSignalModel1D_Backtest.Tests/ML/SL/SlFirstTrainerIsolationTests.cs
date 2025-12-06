using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.SL
	{
	/// <summary>
	/// Изолированные тесты для SlFirstTrainer:
	/// проверяем, что модель чувствительна к целевой разметке и
	/// качество заметно падает при перетасовке Label.
	/// </summary>
	public sealed class SlFirstTrainerIsolationTests
		{
		private sealed class SlHitEvalRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Weight { get; set; }
			}

		[Fact]
		public void Train_QualityDrops_WhenTrainLabelsAreShuffled ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var samplesSignal = BuildSyntheticSamples (
				count: 400,
				startUtc: start);

			var asOfUtc = start.AddDays (500);

			// 1. Нормальное обучение: Label и фичи согласованы.
			var trainer = new SlFirstTrainer ();
			var modelSignal = trainer.Train (samplesSignal, asOfUtc);

			var metricsSignal = Evaluate (modelSignal, samplesSignal);

			Assert.True (metricsSignal.Accuracy > 0.95,
				$"Expected high accuracy for synthetic SL dataset, got {metricsSignal.Accuracy:0.000}");

			// 2. Обучение на перетасованной разметке при тех же фичах.
			var samplesShuffled = CloneSamplesWithShuffledLabel (samplesSignal, seed: 123);

			var trainerShuffled = new SlFirstTrainer ();
			var modelShuffled = trainerShuffled.Train (samplesShuffled, asOfUtc);

			var metricsShuffled = Evaluate (modelShuffled, samplesSignal);

			Assert.True (metricsShuffled.Accuracy < 0.8,
				$"Accuracy with shuffled labels should drop, got {metricsShuffled.Accuracy:0.000}");
			}

		private static BinaryClassificationMetrics Evaluate (
			ITransformer model,
			IEnumerable<SlHitSample> evalSamples )
			{
			var ml = new MLContext (seed: 42);

			var rows = evalSamples.Select (s => new SlHitEvalRow
				{
				Label = s.Label,
				Features = EnsureFixedLength (s.Features),
				Weight = 1.0f
				});

			var data = ml.Data.LoadFromEnumerable (rows);
			var transformed = model.Transform (data);

			return ml.BinaryClassification.Evaluate (
				transformed,
				labelColumnName: nameof (SlHitEvalRow.Label),
				scoreColumnName: "Score",
				predictedLabelColumnName: "PredictedLabel");
			}

		private static List<SlHitSample> BuildSyntheticSamples ( int count, DateTime startUtc )
			{
			var list = new List<SlHitSample> (count);

			for (int i = 0; i < count; i++)
				{
				bool label = i % 2 == 0;

				var feats = new float[MlSchema.FeatureCount];
				// Фича [0] напрямую кодирует Label.
				feats[0] = label ? 1f : -1f;

				list.Add (new SlHitSample
					{
					Label = label,
					Features = feats,
					EntryUtc = startUtc.AddDays (i)
					});
				}

			return list;
			}

		private static List<SlHitSample> CloneSamplesWithShuffledLabel (
			IReadOnlyList<SlHitSample> source,
			int seed )
			{
			var rng = new Random (seed);
			var indices = Enumerable.Range (0, source.Count).ToArray ();

			for (int i = indices.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(indices[i], indices[j]) = (indices[j], indices[i]);
				}

			var result = new List<SlHitSample> (source.Count);

			for (int i = 0; i < source.Count; i++)
				{
				var src = source[i];
				bool newLabel = source[indices[i]].Label;

				result.Add (new SlHitSample
					{
					Label = newLabel,
					Features = (float[]) src.Features.Clone (),
					EntryUtc = src.EntryUtc
					});
				}

			return result;
			}

		private static float[] EnsureFixedLength ( float[] features )
			{
			if (features.Length == MlSchema.FeatureCount)
				return features;

			var arr = new float[MlSchema.FeatureCount];
			Array.Copy (features, arr, Math.Min (features.Length, arr.Length));
			return arr;
			}
		}
	}
