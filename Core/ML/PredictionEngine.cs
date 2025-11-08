using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Обёртка над натрененным бандлом. Никакого System.Data здесь нет.
	/// </summary>
	public sealed class PredictionEngine
		{
		private readonly ModelBundle _bundle;

		private const float FlatMicroProbThresh = 0.60f;

		public PredictionEngine ( ModelBundle bundle )
			{
			_bundle = bundle;
			}

		public (int cls, double[] probs, string reason, MicroInfo micro) Predict ( DataRow r )
			{
			// если у нас есть MLContext и модели — работаем по двухшаговой схеме
			if (_bundle.MlCtx != null && _bundle.MoveModel != null)
				{
				var ml = _bundle.MlCtx;

				// 1) будет ли ход
				var moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);
				var moveOut = moveEng.Predict (new MlSampleBinary
					{
					Features = r.Features.Select (f => (float) f).ToArray ()
					});

				// хода нет → боковик → пробуем микро
				if (!moveOut.PredictedLabel)
					{
					if (_bundle.MicroFlatModel != null)
						{
						var microEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MicroFlatModel);
						var microOut = microEng.Predict (new MlSampleBinary
							{
							Features = r.Features.Select (f => (float) f).ToArray ()
							});

						float p = microOut.Probability;
						bool isFactMicro = r.FactMicroUp || r.FactMicroDown;

						if (isFactMicro && p >= FlatMicroProbThresh)
							{
							bool predUp = microOut.PredictedLabel;
							bool correct = (predUp && r.FactMicroUp) || (!predUp && r.FactMicroDown);

							if (predUp)
								{
								return (1,
									new double[] { 0.05, 0.7, 0.25 },
									"2stage:flat+microUp",
									new MicroInfo
										{
										Predicted = true,
										Up = true,
										ConsiderUp = true,
										ConsiderDown = false,
										Prob = p,
										Correct = correct
										});
								}
							else
								{
								return (1,
									new double[] { 0.25, 0.7, 0.05 },
									"2stage:flat+microDown",
									new MicroInfo
										{
										Predicted = true,
										Up = false,
										ConsiderUp = false,
										ConsiderDown = true,
										Prob = p,
										Correct = correct
										});
								}
							}
						}

					// просто боковик
					return (1, new double[] { 0.05, 0.9, 0.05 }, "2stage:flat", new MicroInfo ());
					}

				// 2) ход есть → берём модель направления по режиму
				var dirModel = r.RegimeDown ? _bundle.DirModelDown : _bundle.DirModelNormal;
				if (dirModel != null)
					{
					var dirEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (dirModel);
					var dirOut = dirEng.Predict (new MlSampleBinary
						{
						Features = r.Features.Select (f => (float) f).ToArray ()
						});

					if (dirOut.PredictedLabel)
						{
						// вверх
						return (2, new double[] { 0.05, 0.05, 0.9 }, "2stage:move-up", new MicroInfo ());
						}
					else
						{
						// вниз
						return (0, new double[] { 0.9, 0.05, 0.05 }, "2stage:move-down", new MicroInfo ());
						}
					}

				// если направления нет — пусть будет плоско
				return (1, new double[] { 0.1, 0.8, 0.1 }, "2stage:no-dir", new MicroInfo ());
				}

			// fallback
			return (1, new double[] { 0.05, 0.9, 0.05 }, "fallback", new MicroInfo ());
			}

		/// <summary>
		/// твой "микро-зачёт"
		/// </summary>
		public bool EvalMicroAware ( DataRow r, int predClass, MicroInfo micro )
			{
			bool baseCorrect = predClass == r.Label;
			if (baseCorrect) return true;

			if (r.Label == 2 && predClass == 1 && micro.ConsiderUp) return true;
			if (r.Label == 0 && predClass == 1 && micro.ConsiderDown) return true;
			if (r.Label == 1 && r.FactMicroUp && predClass == 2) return true;
			if (r.Label == 1 && r.FactMicroDown && predClass == 0) return true;
			if (r.Label == 1 && r.FactMicroUp && predClass == 1 && micro.ConsiderUp) return true;
			if (r.Label == 1 && r.FactMicroDown && predClass == 1 && micro.ConsiderDown) return true;

			return false;
			}

		/// <summary>
		/// твоя весовая оценка
		/// </summary>
		public double EvalWeighted ( DataRow r, int predClass, MicroInfo micro )
			{
			int fact = r.Label;
			bool predMicroUp = micro.ConsiderUp;
			bool predMicroDown = micro.ConsiderDown;
			bool factMicroUp = r.FactMicroUp;
			bool factMicroDown = r.FactMicroDown;

			if (fact == 2)
				{
				if (predClass == 2) return 1.0;
				if (predClass == 1 && predMicroUp) return 1.0;
				if (predClass == 1) return 0.25;
				return 0.0;
				}
			if (fact == 0)
				{
				if (predClass == 0) return 1.0;
				if (predClass == 1 && predMicroDown) return 1.0;
				if (predClass == 1) return 0.25;
				return 0.0;
				}
			if (fact == 1 && factMicroUp)
				{
				if (predClass == 1 && predMicroUp) return 1.0;
				if (predClass == 2) return 0.8;
				if (predClass == 1) return 0.2;
				return 0.0;
				}
			if (fact == 1 && factMicroDown)
				{
				if (predClass == 1 && predMicroDown) return 1.0;
				if (predClass == 0) return 0.8;
				if (predClass == 1) return 0.2;
				return 0.0;
				}
			if (fact == 1)
				{
				if (predClass == 1) return 1.0;
				return 0.3;
				}
			return 0.0;
			}
		}
	}
