using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML
	{
	/// <summary>
	/// Обёртка над натрененным бандлом.
	/// </summary>
	public sealed class PredictionEngine
		{
		private readonly ModelBundle _bundle;
		private const float FlatMicroProbThresh = 0.60f;

		public readonly struct PredResult
			{
			public PredResult ( int cls, string reason, MicroInfo micro )
				{
				Class = cls;
				Reason = reason;
				Micro = micro;
				}

			public int Class { get; }
			public string Reason { get; }
			public MicroInfo Micro { get; }
			}

		public PredictionEngine ( ModelBundle bundle )
			{
			_bundle = bundle;
			}

		public PredResult Predict ( DataRow r )
			{
			// если есть ML — двухшаговая схема
			if (_bundle.MlCtx != null && _bundle.MoveModel != null)
				{
				var ml = _bundle.MlCtx;

				// 1) будет ли ход
				var moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);
				var moveOut = moveEng.Predict (new MlSampleBinary
					{
					Features = r.Features.Select (f => (float) f).ToArray ()
					});

				// хода нет → микрослой (БЕЗ проверки факта!)
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

						// Предполагаем: true => microUp, false => microDown
						if (p >= FlatMicroProbThresh)
							{
							if (microOut.PredictedLabel)
								{
								return new PredResult (
									1,
									"2stage:flat+microUp",
									new MicroInfo
										{
										Predicted = true,
										Up = true,
										ConsiderUp = true,
										ConsiderDown = false,
										Prob = p,
										// Correct заполняем только если есть разметка факта
										Correct = r.FactMicroUp
										});
								}
							else
								{
								return new PredResult (
									1,
									"2stage:flat+microDown",
									new MicroInfo
										{
										Predicted = true,
										Up = false,
										ConsiderUp = false,
										ConsiderDown = true,
										Prob = p,
										Correct = r.FactMicroDown
										});
								}
							}
						}

					// просто боковик
					return new PredResult (
						1,
						"2stage:flat",
						new MicroInfo ());
					}

				// 2) ход есть → берём направление по режиму
				var dirModel = r.RegimeDown ? _bundle.DirModelDown : _bundle.DirModelNormal;
				if (dirModel != null)
					{
					var dirEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (dirModel);
					var dirOut = dirEng.Predict (new MlSampleBinary
						{
						Features = r.Features.Select (f => (float) f).ToArray ()
						});

					bool wantsUp = dirOut.PredictedLabel;

					if (wantsUp)
						{
						// Фон. фильтр по BTC
						bool btcEmaDown = r.BtcEma50vs200 < -0.002;
						bool btcShortRed = r.BtcRet1 < 0 && r.BtcRet30 < 0;
						if (btcEmaDown && btcShortRed)
							{
							return new PredResult (
								1,
								"2stage:move-up-blocked-by-btc-ema",
								new MicroInfo ());
							}

						return new PredResult (2, "2stage:move-up", new MicroInfo ());
						}
					else
						{
						return new PredResult (0, "2stage:move-down", new MicroInfo ());
						}
					}

				// нет модели направления — считаем боковиком
				return new PredResult (1, "2stage:no-dir", new MicroInfo ());
				}

			// fallback
			return new PredResult (1, "fallback", new MicroInfo ());
			}

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
