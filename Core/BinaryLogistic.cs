using SolSignalModel1D_Backtest.Core;
using System;

namespace SolSignalModel1D_Backtest
	{
	public class BinaryLogistic
		{
		private readonly int _feat;
		private readonly double[] _w;
		private bool _trained;

		public BinaryLogistic ( int feat )
			{
			_feat = feat;
			_w = new double[feat + 1];
			var rnd = new Random (123);
			for (int i = 0; i < feat; i++)
				_w[i] = (rnd.NextDouble () - 0.5) * 0.02;
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
					for (int f = 0; f < _feat; f++)
						_w[f] -= lr * grad * r.Features[f];
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
			for (int i = 0; i < x.Length; i++)
				s += w[i] * x[i];
			s += w[^1];
			return s;
			}

		private static double Sigmoid ( double z ) => 1.0 / (1.0 + Math.Exp (-z));
		}
	}
