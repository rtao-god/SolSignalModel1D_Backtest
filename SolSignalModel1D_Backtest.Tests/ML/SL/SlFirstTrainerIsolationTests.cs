using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.SL
	{
	/// <summary>
	/// Изолированные тесты для SlFirstTrainer:
	/// проверяем, что модель чувствительна к целевой разметке и
	/// качество падает при перетасовке Label.
	/// </summary>
	public sealed class SlFirstTrainerIsolationTests
		{
		[Fact]
		public void Train_QualityDrops_WhenTrainLabelsAreShuffled ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var samplesSignal = BuildSyntheticSamples (
				count: 400,
				startUtc: start);

			var asOfUtc = start.AddDays (500);

			var trainer = new SlFirstTrainer ();
			var modelSignal = trainer.Train (samplesSignal, asOfUtc);

			var metricsSignal = Evaluate (modelSignal, samplesSignal);

			Assert.True (
				metricsSignal.Accuracy > 0.95,
				$"Expected high accuracy for synthetic SL dataset, got {metricsSignal.Accuracy:0.000}");

			var samplesShuffled = CloneSamplesWithShuffledLabel (samplesSignal, seed: 123);

			var trainerShuffled = new SlFirstTrainer ();
			var modelShuffled = trainerShuffled.Train (samplesShuffled, asOfUtc);

			var metricsShuffled = Evaluate (modelShuffled, samplesSignal);

			Assert.True (
				metricsShuffled.Accuracy < 0.8,
				$"Accuracy with shuffled labels should drop, got {metricsShuffled.Accuracy:0.000}");
			}

		private static BinaryClassificationMetrics Evaluate (
			ITransformer model,
			IEnumerable<SlHitSample> evalSamples )
			{
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (evalSamples == null) throw new ArgumentNullException (nameof (evalSamples));

			var ml = new MLContext (seed: 42);

			var list = evalSamples.ToList ();
			if (list.Count == 0)
				throw new InvalidOperationException ("[test] SL eval dataset is empty.");

			for (int i = 0; i < list.Count; i++)
				ValidateSampleOrThrow (list[i], i);

			var data = ml.Data.LoadFromEnumerable (list);
			var transformed = model.Transform (data);

			return ml.BinaryClassification.Evaluate (
				transformed,
				labelColumnName: nameof (SlHitSample.Label),
				scoreColumnName: "Score",
				predictedLabelColumnName: "PredictedLabel");
			}

		private static List<SlHitSample> BuildSyntheticSamples ( int count, DateTime startUtc )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("startUtc must be UTC.", nameof (startUtc));
			if (count <= 0)
				throw new ArgumentOutOfRangeException (nameof (count), count, "count must be > 0.");

			var list = new List<SlHitSample> (count);

			for (int i = 0; i < count; i++)
				{
				bool label = i % 2 == 0;

				var feats = new float[MlSchema.FeatureCount];
				feats[0] = label ? 1f : -1f;

				list.Add (new SlHitSample
					{
					Label = label,
					Features = feats,
					EntryUtc = startUtc.AddDays (i),
					Weight = 1.0f
					});
				}

			for (int i = 0; i < list.Count; i++)
				ValidateSampleOrThrow (list[i], i);

			return list;
			}

		private static List<SlHitSample> CloneSamplesWithShuffledLabel (
			IReadOnlyList<SlHitSample> source,
			int seed )
			{
			if (source == null) throw new ArgumentNullException (nameof (source));
			if (source.Count == 0) throw new InvalidOperationException ("[test] source is empty.");

			for (int i = 0; i < source.Count; i++)
				ValidateSampleOrThrow (source[i], i);

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

				result.Add (new SlHitSample
					{
					Label = source[indices[i]].Label,
					Features = (float[]) src.Features.Clone (),
					EntryUtc = src.EntryUtc,
					Weight = src.Weight
					});
				}

			for (int i = 0; i < result.Count; i++)
				ValidateSampleOrThrow (result[i], i);

			return result;
			}

		private static void ValidateSampleOrThrow ( SlHitSample s, int idx )
			{
			if (s == null)
				throw new InvalidOperationException ($"[test] SlHitSample is null at idx={idx}.");

			if (s.EntryUtc == default)
				throw new InvalidOperationException ($"[test] SlHitSample.EntryUtc is default at idx={idx}.");

			if (s.EntryUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[test] SlHitSample.EntryUtc must be UTC at idx={idx}: {s.EntryUtc:O}.");

			if (s.Features == null)
				throw new InvalidOperationException ($"[test] SlHitSample.Features is null at idx={idx}.");

			if (s.Features.Length != MlSchema.FeatureCount)
				throw new InvalidOperationException (
					$"[test] SlHitSample.Features length mismatch at idx={idx}: len={s.Features.Length}, expected={MlSchema.FeatureCount}.");

			for (int j = 0; j < s.Features.Length; j++)
				{
				float v = s.Features[j];
				if (float.IsNaN (v) || float.IsInfinity (v))
					throw new InvalidOperationException ($"[test] Non-finite feature at sample idx={idx}, featureIdx={j}: {v}.");
				}

			if (float.IsNaN (s.Weight) || float.IsInfinity (s.Weight))
				throw new InvalidOperationException ($"[test] SlHitSample.Weight must be finite at idx={idx}: {s.Weight}.");

			if (s.Weight < 0f)
				throw new InvalidOperationException ($"[test] SlHitSample.Weight must be >= 0 at idx={idx}: {s.Weight}.");
			}
		}
	}
