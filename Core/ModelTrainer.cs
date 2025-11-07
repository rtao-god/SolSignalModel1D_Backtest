using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core
	{
	public sealed class ModelTrainer
		{
		private const int LogEpochs = 120;
		private const double LogLr = 0.08;
		private const double RareClassBoost = 1.35;

		public ModelBundle TrainAll (
			List<DataRow> rows,
			HashSet<DateTime> testDates,
			int downLimit,
			int normalLimit )
			{
			var downRows = rows.Where (r => r.RegimeDown).ToList ();
			var normalRows = rows.Where (r => !r.RegimeDown).ToList ();

			int featCount = rows.First ().Features.Length;

			var downTrain = downRows
				.Where (r => !testDates.Contains (r.Date))
				.OrderByDescending (r => r.Date)
				.Take (downLimit)
				.ToList ();

			var normalTrain = normalRows
				.Where (r => !testDates.Contains (r.Date))
				.OrderByDescending (r => r.Date)
				.Take (normalLimit)
				.ToList ();

			var downModel = new OvrLogistic (featCount, 3, RareClassBoost);
			var normalModel = new OvrLogistic (featCount, 3, RareClassBoost);

			if (downTrain.Count > 10)
				downModel.TrainWeighted (downTrain, LogEpochs, LogLr);
			if (normalTrain.Count > 10)
				normalModel.TrainWeighted (normalTrain, LogEpochs, LogLr);

			// micro — только NORMAL
			var microTrain = new List<DataRow> ();
			foreach (var r in normalRows.OrderByDescending (r => r.Date))
				{
				if (testDates.Contains (r.Date)) continue;
				if (Math.Abs (r.SolFwd1) < r.MinMove)
					{
					double band = r.MinMove * 0.20;
					if (Math.Abs (r.SolFwd1) >= band)
						microTrain.Add (r);
					}
				}

			BinaryLogistic? microModel = null;
			if (microTrain.Count > 15)
				{
				microModel = new BinaryLogistic (featCount);
				microModel.Train (microTrain, 120, 0.08);
				Console.WriteLine ($"[micro] обучено на {microTrain.Count} боковиках (NORMAL-only, newest-first)");
				}
			else
				{
				Console.WriteLine ("[micro] мало данных для обучения микро-направления");
				}

			Console.WriteLine ($"[train] down-regime accuracy: {(downTrain.Count == 0 ? 0 : 100.0 * EvalTrain (downModel, downTrain) / downTrain.Count):0.0}% ({EvalTrain (downModel, downTrain)}/{downTrain.Count})");
			Console.WriteLine ($"[train] normal-regime accuracy: {(normalTrain.Count == 0 ? 0 : 100.0 * EvalTrain (normalModel, normalTrain) / normalTrain.Count):0.0}% ({EvalTrain (normalModel, normalTrain)}/{normalTrain.Count})");

			return new ModelBundle
				{
				DownModel = downModel,
				NormalModel = normalModel,
				MicroModel = microModel
				};
			}

		private static int EvalTrain ( OvrLogistic model, List<DataRow> rows )
			{
			int ok = 0;
			foreach (var r in rows)
				{
				var probs = model.PredictProba (r.Features);
				int argmax = Array.IndexOf (probs, probs.Max ());
				if (argmax == r.Label) ok++;
				}
			return ok;
			}
		}


	public sealed class OvrLogistic
		{
		private readonly int _classes;
		private readonly int _feat;
		private readonly double[][] _w;
		private readonly double _rareBoost;

		public OvrLogistic ( int featureCount, int classCount, double rareBoost )
			{
			_classes = classCount;
			_feat = featureCount;
			_rareBoost = rareBoost;
			_w = new double[classCount][];
			var rnd = new Random (42);
			for (int c = 0; c < classCount; c++)
				{
				_w[c] = new double[featureCount + 1];
				for (int f = 0; f < featureCount; f++)
					_w[c][f] = (rnd.NextDouble () - 0.5) * 0.02;
				}
			}

		public void TrainWeighted ( List<DataRow> rows, int epochs, double lr )
			{
			if (rows.Count == 0) return;
			var counts = new double[_classes];
			foreach (var r in rows) counts[r.Label]++;
			double maxCnt = counts.Max ();
			var classWeights = new double[_classes];
			for (int c = 0; c < _classes; c++)
				{
				double w = maxCnt / Math.Max (1.0, counts[c]);
				if (c == 0 || c == 2) w *= _rareBoost;
				classWeights[c] = w;
				}

			for (int ep = 0; ep < epochs; ep++)
				{
				foreach (var r in rows)
					{
					for (int c = 0; c < _classes; c++)
						{
						double z = Dot (_w[c], r.Features);
						double yhat = Sigmoid (z);
						double y = (r.Label == c) ? 1.0 : 0.0;
						double grad = (yhat - y) * classWeights[c];
						for (int f = 0; f < _feat; f++)
							_w[c][f] -= lr * grad * r.Features[f];
						_w[c][_feat] -= lr * grad;
						}
					}
				}
			}

		public double[] PredictProba ( double[] feats )
			{
			double[] probs = new double[_classes];
			for (int c = 0; c < _classes; c++)
				{
				double z = Dot (_w[c], feats);
				probs[c] = Sigmoid (z);
				}
			double s = probs.Sum ();
			if (s > 0) for (int i = 0; i < probs.Length; i++) probs[i] /= s;
			return probs;
			}

		private static double Dot ( double[] w, double[] x )
			{
			double s = 0;
			for (int i = 0; i < x.Length; i++) s += w[i] * x[i];
			s += w[^1];
			return s;
			}

		private static double Sigmoid ( double z ) => 1.0 / (1.0 + Math.Exp (-z));
		}

	public sealed class BinaryLogistic
		{
		private readonly int _feat;
		private readonly double[] _w;
		private bool _trained = false;

		public BinaryLogistic ( int feat )
			{
			_feat = feat;
			_w = new double[feat + 1];
			var rnd = new Random (123);
			for (int i = 0; i < feat; i++) _w[i] = (rnd.NextDouble () - 0.5) * 0.02;
			}

		public void Train ( List<DataRow> rows, int epochs, double lr )
			{
			if (rows.Count == 0) return;
			for (int ep = 0; ep < epochs; ep++)
				{
				foreach (var r in rows)
					{
					double y = r.SolFwd1 > 0 ? 1.0 : 0.0;
					double z = Dot (_w, r.Features);
					double p = Sigmoid (z);
					double grad = (p - y);
					for (int f = 0; f < _feat; f++) _w[f] -= lr * grad * r.Features[f];
					_w[_feat] -= lr * grad;
					}
				}
			_trained = true;
			}

		public (int cls, double prob) Predict ( double[] feats )
			{
			if (!_trained) return (1, 0.5);
			double z = Dot (_w, feats);
			double p = Sigmoid (z);
			int cls = p >= 0.5 ? 1 : 0;
			return (cls, p);
			}

		private static double Dot ( double[] w, double[] x )
			{
			double s = 0;
			for (int i = 0; i < x.Length; i++) s += w[i] * x[i];
			s += w[^1];
			return s;
			}

		private static double Sigmoid ( double z ) => 1.0 / (1.0 + Math.Exp (-z));
		}
	}
