using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Analytics.ML;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// SL-классификатор: учим только на прошлых сэмплах, свежим — больший вес,
	/// и дополнительно усиливаем TP до x3, чтобы модель не выкидывала хорошие дни.
	/// </summary>
	public sealed class SlFirstTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		private sealed class SlHitTrainRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Weight { get; set; }
			}

		public ITransformer Train ( List<SlHitSample> samples, DateTime asOfUtc )
			{
			if (samples == null || samples.Count == 0)
				throw new InvalidOperationException ("No SL samples to train.");

			var trainRows = new List<SlHitTrainRow> (samples.Count);

			foreach (var s in samples)
				{
				double ageDays = (asOfUtc - s.EntryUtc).TotalDays;
				if (ageDays < 0) ageDays = 0;
				double ageMonths = ageDays / 30.0;

				// затухание по времени
				float timeWeight =
					ageMonths <= 3.0 ? 1.0f :
					ageMonths <= 6.0 ? 0.7f :
					ageMonths <= 12.0 ? 0.4f : 0.2f;

				trainRows.Add (new SlHitTrainRow
					{
					Label = s.Label,
					Features = PadToFixed (s.Features),
					Weight = timeWeight
					});
				}

			int slCount = trainRows.Count (r => r.Label);
			int tpCount = trainRows.Count - slCount;

			if (slCount > 0 && tpCount > 0)
				{
				// у нас обычно SL > TP → поднимаем TP сильнее, до x3
				if (tpCount < slCount)
					{
					// ratio = во сколько раз SL больше TP
					double ratio = slCount / (double) tpCount;
					float mul = (float) Math.Min (ratio, 3.0); // максимум x3
					foreach (var r in trainRows.Where (x => !x.Label))
						r.Weight *= mul;
					}
				else if (slCount < tpCount)
					{
					// наоборот сильно не надо, но чуть можно
					double ratio = tpCount / (double) slCount;
					float mul = (float) Math.Min (ratio, 1.5);
					foreach (var r in trainRows.Where (x => x.Label))
						r.Weight *= mul;
					}
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
			Console.WriteLine ($"[sl-model] trained on {trainRows.Count} samples (SL={slCount}, TP={tpCount}) asOf={asOfUtc:yyyy-MM-dd}");
			return model;
			}

		public PredictionEngine<SlHitSample, SlHitPrediction> CreateEngine ( ITransformer model )
			{
			return _ml.Model.CreatePredictionEngine<SlHitSample, SlHitPrediction> (model);
			}

		private static float[] PadToFixed ( float[]? src )
			{
			var arr = new float[MlSchema.FeatureCount];
			if (src == null) return arr;
			int len = Math.Min (src.Length, MlSchema.FeatureCount);
			Array.Copy (src, arr, len);
			return arr;
			}

		/// <summary>
		/// PFI + direction для SL-модели на произвольном наборе SlHitSample
		/// (train / test / holdout). Весами здесь не пользуемся — все Weight=1.
		/// Модель не переобучается, только логируется важность фич.
		/// </summary>
		public void LogFeatureImportance (
			ITransformer model,
			IEnumerable<SlHitSample> evalSamples,
			string tag = "sl-oos" )
			{
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (evalSamples == null) throw new ArgumentNullException (nameof (evalSamples));

			var list = evalSamples.ToList ();
			if (list.Count == 0)
				{
				Console.WriteLine ($"[pfi:{tag}] SL eval dataset is empty, skip.");
				return;
				}

			// Для анализа дублируем структуру train-строки, но Weight ставим = 1.
			var rows = list
				.Select (s => new SlHitTrainRow
					{
					Label = s.Label,
					Features = PadToFixed (s.Features),
					Weight = 1.0f
					})
				.ToList ();

			var data = _ml.Data.LoadFromEnumerable (rows);

			FeatureImportanceAnalyzer.LogBinaryFeatureImportance (
				_ml,
				model,
				data,
				SlFeatureSchema.Names,
				tag);
			}
		}
	}
