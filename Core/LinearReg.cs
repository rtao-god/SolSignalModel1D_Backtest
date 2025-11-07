namespace SolSignalModel1D_Backtest.Core
	{
	public class LinearReg
		{
		private readonly int _feat;
		private readonly double[] _w;

		public LinearReg ( int feat )
			{
			_feat = feat;
			_w = new double[feat + 1];
			}

		public void Train ( List<DataRow> rows, int epochs, double lr, double l2 )
			{
			if (rows.Count == 0) return;
			for (int ep = 0; ep < epochs; ep++)
				{
				foreach (var r in rows)
					{
					double y = r.SolFwd1;
					double yhat = Predict (r.Features);
					double err = yhat - y;
					for (int f = 0; f < _feat; f++)
						_w[f] -= lr * (err * r.Features[f] + l2 * _w[f]);
					_w[_feat] -= lr * err;
					}
				}
			}

		public double Predict ( double[] x )
			{
			double s = 0;
			for (int i = 0; i < _feat; i++)
				s += _w[i] * x[i];
			s += _w[_feat];
			return s;
			}
		}
	}
