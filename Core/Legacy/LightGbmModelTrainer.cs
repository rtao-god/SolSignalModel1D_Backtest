using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Legacy
	{
	public sealed class LightGbmModelTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);

		// считаем рынок актуальным с этой даты
		private static readonly DateTime RecentCutoff = new DateTime (2025, 1, 1);

		// LightGBM на малом датасете — скромные параметры
		private const int Leaves = 12;
		private const int Iters = 80;
		private const float Lr = 0.07f;
		private const int MinDataInLeaf = 20;

		// ретро-записям даём меньший вес
		private const float OldRecencyWeight = 0.4f;

		public ModelBundle TrainAll (
			List<DataRow> rows,
			HashSet<DateTime> testDates,
			int downLimit,
			int normalLimit )
			{
			var downAll = rows.Where (r => r.RegimeDown && !testDates.Contains (r.Date))
							  .OrderByDescending (r => r.Date)
							  .ToList ();

			var normalAll = rows.Where (r => !r.RegimeDown && !testDates.Contains (r.Date))
								.OrderByDescending (r => r.Date)
								.ToList ();

			var downModel = BuildRegimeModel (downAll, "down");
			var normalModel = BuildRegimeModel (normalAll, "normal");
			var microModel = BuildMicroModel (normalAll);

			return new ModelBundle
				{
				};
			}

		private ITransformer? BuildRegimeModel ( List<DataRow> regimeRows, string tag )
			{
			if (regimeRows.Count == 0)
				{
				Console.WriteLine ($"[{tag}] нет строк");
				return null;
				}

			// делим только для вычисления ВЕСОВ — НЕ для отбора строк
			var recent = regimeRows.Where (r => r.Date >= RecentCutoff).ToList ();
			if (recent.Count == 0) recent = regimeRows; // если вдруг нет свежих — считаем по всему

			// ----- КЛАССОВЫЕ ВЕСА (без семплинга, по свежим) -----
			// w_c = N_recent / (K * n_c), затем кап [0.5, 2.0]
			int K = 3;
			int N_recent = recent.Count;
			var nByClass = new int[K];
			foreach (var r in recent) nByClass[r.Label]++;

			var classWeight = new float[K];
			for (int c = 0; c < K; c++)
				{
				// если класса нет — дайте ему средний вес 1.0 (не раздуваем)
				if (nByClass[c] <= 0) { classWeight[c] = 1.0f; continue; }
				float w = (float) N_recent / (K * nByClass[c]);
				if (w < 0.5f) w = 0.5f;
				if (w > 2.0f) w = 2.0f;
				classWeight[c] = w;
				}

			// ----- БЕЗ oversample: вся выборка режима идёт в модель -----
			// вес объекта = classWeight[label] * recencyWeight
			var data = _ml.Data.LoadFromEnumerable (
				regimeRows.Select (r =>
				{
					float recW = r.Date >= RecentCutoff ? 1.0f : OldRecencyWeight;
					float clsW = classWeight[r.Label];
					return new MlSampleWeighted
						{
						Label = r.Label,
						Features = r.Features.Select (f => (float) f).ToArray (),
						Weight = recW * clsW
						};
				})
			);

			Console.WriteLine ($"[{tag}] LightGBM train rows: {regimeRows.Count} (recent for weights: {recent.Count})");

			var pipe =
				_ml.Transforms.Conversion.MapValueToKey ("Label")
				.Append (_ml.MulticlassClassification.Trainers.LightGbm (
					new LightGbmMulticlassTrainer.Options
						{
						NumberOfLeaves = Leaves,
						NumberOfIterations = Iters,
						LearningRate = Lr,
						MinimumExampleCountPerLeaf = MinDataInLeaf,
						ExampleWeightColumnName = "Weight"
						}))
				.Append (_ml.Transforms.Conversion.MapKeyToValue ("PredictedLabel"));

			return pipe.Fit (data);
			}

		private ITransformer? BuildMicroModel ( List<DataRow> normalAll )
			{
			var rows = new List<DataRow> ();
			foreach (var r in normalAll.OrderByDescending (r => r.Date))
				{
				if (Math.Abs (r.SolFwd1) < r.MinMove)
					{
					double band = r.MinMove * 0.20;
					if (Math.Abs (r.SolFwd1) >= band)
						rows.Add (r);
					}
				}

			if (rows.Count < 20)
				{
				Console.WriteLine ("[micro] мало строк, скипаем");
				return null;
				}

			var data = _ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.SolFwd1 >= 0,
					Features = r.Features.Select (f => (float) f).ToArray ()
					})
			);

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 12,
					NumberOfIterations = 80,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 20
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[micro] обучено на {rows.Count} боковиках (LightGBM, no-oversample, class-neutral)");
			return model;
			}
		}
	}