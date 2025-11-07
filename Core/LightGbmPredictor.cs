using Microsoft.ML;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core
	{
	public sealed class LightGbmPredictor
		{
		private readonly MLContext _ml;
		private readonly ITransformer? _downModel;
		private readonly ITransformer? _normalModel;
		private readonly ITransformer? _microModel;

		public LightGbmPredictor (
			MLContext ml,
			ITransformer? downModel,
			ITransformer? normalModel,
			ITransformer? microModel )
			{
			_ml = ml;
			_downModel = downModel;
			_normalModel = normalModel;
			_microModel = microModel;
			}

		// скажи снаружи, есть ли модель для этого режима
		public bool HasModel ( bool isDown )
			{
			return isDown ? _downModel != null : _normalModel != null;
			}

		public float[]? TryPredictProba ( double[] feats, bool isDown )
			{
			ITransformer? model = isDown ? _downModel : _normalModel;
			if (model == null)
				return null;

			var engine = _ml.Model.CreatePredictionEngine<MlSample, MlOutput> (model);
			var output = engine.Predict (new MlSample
				{
				Features = feats.Select (f => (float) f).ToArray ()
				});

			return output.Score;
			}

		public (int cls, float prob)? TryPredictMicro ( double[] feats )
			{
			if (_microModel == null)
				return null;

			var engine = _ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_microModel);
			var output = engine.Predict (new MlSampleBinary
				{
				Features = feats.Select (f => (float) f).ToArray ()
				});

			return (output.PredictedLabel ? 1 : 0, output.Probability);
			}
		}
	}
