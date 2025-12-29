using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.SL
	{
	public sealed class SlFirstTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		private sealed class SlHitTrainRow
			{
			public bool Label { get; set; }

			[VectorType (SlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[SlSchema.FeatureCount];

			public float Weight { get; set; }
			}

		public ITransformer Train ( List<SlHitSample> samples, DateTime asOfUtc )
			{
			if (samples == null || samples.Count == 0)
				throw new InvalidOperationException ("[sl-model] No SL samples to train.");

			if (asOfUtc == default || asOfUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("asOfUtc must be initialized and UTC.", nameof (asOfUtc));

			var trainRows = new List<SlHitTrainRow> (samples.Count);

			foreach (var s in samples)
				{
				if (s.EntryUtc == default)
					throw new InvalidOperationException ("[sl-model] SlHitSample.EntryUtc is default.");

				if (s.EntryUtc.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[sl-model] SlHitSample.EntryUtc must be UTC: {s.EntryUtc:O}.");

				if (s.EntryUtc > asOfUtc)
					throw new InvalidOperationException (
						$"[sl-model] Sample EntryUtc is later than asOfUtc: entry={s.EntryUtc:O}, asOf={asOfUtc:O}.");

				double ageDays = (asOfUtc - s.EntryUtc).TotalDays;
				double ageMonths = ageDays / 30.0;

				float timeWeight =
					ageMonths <= 3.0 ? 1.0f :
					ageMonths <= 6.0 ? 0.7f :
					ageMonths <= 12.0 ? 0.4f : 0.2f;

				trainRows.Add (new SlHitTrainRow
					{
					Label = s.Label,
					Features = CopyFixedFeaturesOrThrow (s.Features),
					Weight = timeWeight
					});
				}

			int slCount = trainRows.Count (r => r.Label);
			int tpCount = trainRows.Count - slCount;

			if (slCount == 0 || tpCount == 0)
				throw new InvalidOperationException (
					$"[sl-model] Training set must contain both classes (SL and TP). SL={slCount}, TP={tpCount}.");

			if (tpCount < slCount)
				{
				double ratio = slCount / (double) tpCount;
				float mul = (float) Math.Min (ratio, 3.0f);

				foreach (var r in trainRows.Where (x => !x.Label))
					r.Weight *= mul;
				}
			else if (slCount < tpCount)
				{
				double ratio = tpCount / (double) slCount;
				float mul = (float) Math.Min (ratio, 1.5f);

				foreach (var r in trainRows.Where (x => x.Label))
					r.Weight *= mul;
				}

			var data = _ml.Data.LoadFromEnumerable (trainRows);

			var opts = new LightGbmBinaryTrainer.Options
				{
				NumberOfLeaves = 16,
				NumberOfIterations = 90,
				LearningRate = 0.07f,
				MinimumExampleCountPerLeaf = 15,

				LabelColumnName = nameof (SlHitTrainRow.Label),
				FeatureColumnName = nameof (SlHitTrainRow.Features),
				ExampleWeightColumnName = nameof (SlHitTrainRow.Weight),

				Seed = 42,
				NumberOfThreads = 1
				};

			var model = _ml.BinaryClassification.Trainers.LightGbm (opts).Fit (data);

			Console.WriteLine (
				$"[sl-model] trained on {trainRows.Count} samples (SL={slCount}, TP={tpCount}) asOf={asOfUtc:yyyy-MM-dd}");

			return model;
			}

		public PredictionEngine<SlHitSample, SlHitPrediction> CreateEngine ( ITransformer model )
			{
			if (model == null) throw new ArgumentNullException (nameof (model));
			return _ml.Model.CreatePredictionEngine<SlHitSample, SlHitPrediction> (model);
			}

		public void LogFeatureImportance (
			ITransformer model,
			IEnumerable<SlHitSample> evalSamples,
			string tag = "sl-oos" )
			{
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (evalSamples == null) throw new ArgumentNullException (nameof (evalSamples));

			SlPfiAnalyzer.LogBinaryPfiWithDirection (_ml, model, evalSamples, tag: tag);
			}

		private static float[] CopyFixedFeaturesOrThrow ( float[]? src )
			{
			if (src == null)
				throw new InvalidOperationException ("[sl-model] SlHitSample.Features is null.");

			if (src.Length != SlSchema.FeatureCount)
				throw new InvalidOperationException (
					$"[sl-model] SlHitSample.Features length mismatch: len={src.Length}, expected={SlSchema.FeatureCount}.");

			var arr = new float[SlSchema.FeatureCount];
			Array.Copy (src, arr, SlSchema.FeatureCount);
			return arr;
			}
		}
	}
