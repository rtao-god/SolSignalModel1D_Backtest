using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	public sealed class ModelTrainer
		{
		private readonly MLContext _ml = new MLContext (seed: 42);
		private static readonly DateTime RecentCutoff = new DateTime (2025, 1, 1);

		private static readonly bool BalanceMove = false;
		private static readonly bool BalanceDir = true;
		private const double BalanceTargetFrac = 0.70;

		public ModelBundle TrainAll (
			List<DataRow> trainRows,
			HashSet<DateTime>? datesToExclude = null )
			{
			if (datesToExclude != null && datesToExclude.Count > 0)
				{
				trainRows = trainRows
					.Where (r => !datesToExclude.Contains (r.Date))
					.ToList ();
				}

			trainRows = trainRows.OrderBy (r => r.Date).ToList ();

			// ===== move =====
			List<DataRow> moveTrainRows = trainRows;
			if (BalanceMove)
				{
				moveTrainRows = OversampleBinary (
					trainRows,
					r => Math.Abs (r.SolFwd1) >= r.MinMove,
					BalanceTargetFrac
				);
				}

			var moveData = _ml.Data.LoadFromEnumerable (
				moveTrainRows.Select (r => new MlSampleBinary
					{
					Label = Math.Abs (r.SolFwd1) >= r.MinMove,
					Features = ToFloatFixed (r.Features)
					})
			);

			var movePipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 16,
					NumberOfIterations = 90,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 20,
					Seed = 42,
					NumberOfThreads = 1
					});

			var moveModel = movePipe.Fit (moveData);
			Console.WriteLine ($"[2stage] move-model trained on {moveTrainRows.Count} rows");

			// ===== dir =====
			var moveRows = trainRows
				.Where (r => Math.Abs (r.SolFwd1) >= r.MinMove)
				.OrderBy (r => r.Date)
				.ToList ();

			var dirNormalRows = moveRows.Where (r => !r.RegimeDown).OrderBy (r => r.Date).ToList ();
			var dirDownRows = moveRows.Where (r => r.RegimeDown).OrderBy (r => r.Date).ToList ();

			if (BalanceDir)
				{
				dirNormalRows = OversampleBinary (dirNormalRows, r => r.SolFwd1 > 0, BalanceTargetFrac);
				dirDownRows = OversampleBinary (dirDownRows, r => r.SolFwd1 > 0, BalanceTargetFrac);
				}

			var dirNormalModel = BuildDirModel (dirNormalRows, "dir-normal");
			var dirDownModel = BuildDirModel (dirDownRows, "dir-down");

			// ===== micro =====
			var microModel = BuildMicroFlatModel (trainRows);

			return new ModelBundle
				{
				MoveModel = moveModel,
				DirModelNormal = dirNormalModel,
				DirModelDown = dirDownModel,
				MicroFlatModel = microModel,
				MlCtx = _ml
				};
			}

		private ITransformer? BuildDirModel ( List<DataRow> rows, string tag )
			{
			if (rows.Count < 40)
				{
				Console.WriteLine ($"[2stage] {tag}: мало строк ({rows.Count}), скипаем");
				return null;
				}

			int recent = rows.Count (r => r.Date >= RecentCutoff);

			var data = _ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.SolFwd1 > 0,
					Features = ToFloatFixed (r.Features)
					})
			);

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 16,
					NumberOfIterations = 90,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15,
					Seed = 42,
					NumberOfThreads = 1
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage] {tag}: trained on {rows.Count} rows (recent {recent})");
			return model;
			}

		private ITransformer? BuildMicroFlatModel ( List<DataRow> rows )
			{
			var flats = rows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderBy (r => r.Date)
				.ToList ();

			if (flats.Count < 30)
				{
				Console.WriteLine ("[2stage-micro] мало микро-дней, скипаем");
				return null;
				}

			var up = flats.Where (r => r.FactMicroUp).ToList ();
			var dn = flats.Where (r => r.FactMicroDown).ToList ();
			int take = Math.Min (up.Count, dn.Count);
			if (take > 0)
				{
				up = up.Take (take).OrderBy (r => r.Date).ToList ();
				dn = dn.Take (take).OrderBy (r => r.Date).ToList ();
				flats = up.Concat (dn).OrderBy (r => r.Date).ToList ();
				}

			var data = _ml.Data.LoadFromEnumerable (
				flats.Select (r => new MlSampleBinary
					{
					Label = r.FactMicroUp,
					Features = ToFloatFixed (r.Features)
					})
			);

			var pipe = _ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 12,
					NumberOfIterations = 70,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15,
					Seed = 42,
					NumberOfThreads = 1
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage-micro] обучено на {flats.Count} REAL микро-днях");
			return model;
			}

		private static List<DataRow> OversampleBinary (
			List<DataRow> src,
			Func<DataRow, bool> isPositive,
			double targetFrac )
			{
			var pos = src.Where (isPositive).ToList ();
			var neg = src.Where (r => !isPositive (r)).ToList ();

			if (pos.Count == 0 || neg.Count == 0)
				return src;

			bool posIsMajor = pos.Count >= neg.Count;
			int major = posIsMajor ? pos.Count : neg.Count;
			int minor = posIsMajor ? neg.Count : pos.Count;

			int target = (int) Math.Round (major * targetFrac, MidpointRounding.AwayFromZero);
			if (target <= minor)
				return src;

			var minorList = posIsMajor ? neg : pos;
			var res = new List<DataRow> (src.Count + (target - minor));
			res.AddRange (src);

			int need = target - minor;
			for (int i = 0; i < need; i++)
				res.Add (minorList[i % minorList.Count]);

			return res.OrderBy (r => r.Date).ToList ();
			}

		private static float[] ToFloatFixed ( double[] src )
			{
			var f = new float[MlSchema.FeatureCount];
			int len = Math.Min (src.Length, MlSchema.FeatureCount);
			for (int i = 0; i < len; i++)
				f[i] = (float) src[i];
			return f;
			}
		}
	}
