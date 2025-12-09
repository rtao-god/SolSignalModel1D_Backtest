using System;
using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	public sealed class PredictionEngine
		{
		private readonly ModelBundle _bundle;
		private readonly ProbabilityAggregationConfig _aggConfig;

		public static bool DebugAllowDisabledModels { get; set; } = false;
		public static bool DebugTreatMissingMoveAsFlat { get; set; } = false;
		public static bool DebugTreatMissingDirAsFlat { get; set; } = false;

		private const float FlatMicroProbThresh = 0.60f;
		private const int MicroDebugMaxRows = 10;
		private static int _microDebugPrinted;

		public readonly struct PredResult
			{
			public PredResult (
				int cls,
				string reason,
				MicroInfo micro,
				DailyProbabilities day,
				DailyProbabilities dayWithMicro )
				{
				Class = cls;
				Reason = reason;
				Micro = micro;
				Day = day;
				DayWithMicro = dayWithMicro;
				}

			public int Class { get; }
			public string Reason { get; }
			public MicroInfo Micro { get; }

			/// <summary>P_day: дневные вероятности из move+dir+BTC.</summary>
			public DailyProbabilities Day { get; }

			/// <summary>P_dayMicro: дневные вероятности после микро-оверлея.</summary>
			public DailyProbabilities DayWithMicro { get; }
			}

		public PredictionEngine ( ModelBundle bundle, ProbabilityAggregationConfig? aggregationConfig = null )
			{
			_bundle = bundle ?? throw new ArgumentNullException (nameof (bundle));
			_aggConfig = aggregationConfig ?? new ProbabilityAggregationConfig ();
			}

		public PredResult Predict ( DataRow r )
			{
			try
				{
				if (r == null)
					throw new ArgumentNullException (nameof (r));

				if (r.Features == null)
					{
					throw new InvalidOperationException (
						"[PredictionEngine] DataRow.Features == null. " +
						"Нет фич для дня " + r.Date.ToString ("O"));
					}

				var fixedFeatures = MlTrainingUtils.ToFloatFixed (r.Features);

				if (_bundle.MlCtx == null)
					throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MlCtx == null");

				var ml = _bundle.MlCtx;

				var sample = new MlSampleBinary
					{
					Features = fixedFeatures
					};

				// ===== 1. Бинарная модель "есть ли ход" (move) =====

				MlBinaryOutput moveOut;

				if (_bundle.MoveModel == null)
					{
					if (!DebugAllowDisabledModels)
						{
						throw new InvalidOperationException (
							"[PredictionEngine] ModelBundle.MoveModel == null (нет move-модели)");
						}

					if (DebugTreatMissingMoveAsFlat)
						{
						// Debug: считаем, что всегда flat.
						var rawFlat = new DailyRawOutput
							{
							PMove = 0.0,
							PUpGivenMove = 0.5,
							BtcFilterBlocksUp = false,
							BtcFilterBlocksFlat = false,
							BtcFilterBlocksDown = false
							};

						var dayFlat = DayProbabilityBuilder.BuildDayProbabilities (rawFlat);
						var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);
						var microProbs = ConvertMicro (microInfo);
						var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

						string reason = microInfo.Predicted
							? (microInfo.Up ? "day:flat+microUp(move-disabled)" : "day:flat+microDown(move-disabled)")
							: "day:flat(move-disabled)";

						return new PredResult (1, reason, microInfo, dayFlat, dayFlatMicro);
						}
					else
						{
						// Debug: считаем, что ход есть.
						moveOut = new MlBinaryOutput
							{
							PredictedLabel = true,
							Probability = 1.0f,
							Score = 0.0f
							};
						}
					}
				else
					{
					var moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);
					moveOut = moveEng.Predict (sample);
					}

				// ===== 2. Нет хода → дневной flat + микро-слой =====
				if (!moveOut.PredictedLabel)
					{
					var rawFlat = new DailyRawOutput
						{
						PMove = moveOut.Probability,
						PUpGivenMove = 0.5,
						BtcFilterBlocksUp = false,
						BtcFilterBlocksFlat = false,
						BtcFilterBlocksDown = false
						};

					var dayFlat = DayProbabilityBuilder.BuildDayProbabilities (rawFlat);
					var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);
					var microProbs = ConvertMicro (microInfo);
					var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

					string reason = microInfo.Predicted
						? (microInfo.Up ? "day:flat+microUp" : "day:flat+microDown")
						: "day:flat";

					return new PredResult (1, reason, microInfo, dayFlat, dayFlatMicro);
					}

				// ===== 3. Ход есть → dir-модель =====

				var dirModel = r.RegimeDown && _bundle.DirModelDown != null
					? _bundle.DirModelDown
					: _bundle.DirModelNormal;

				if (dirModel == null)
					{
					if (!DebugAllowDisabledModels)
						{
						throw new InvalidOperationException (
							"[PredictionEngine] DirModelNormal/DirModelDown == null (нет dir-модели)");
						}

					// Debug-фоллбеки без микро: микро здесь не считается.
					if (DebugTreatMissingDirAsFlat)
						{
						var rawFlat = new DailyRawOutput
							{
							PMove = 0.0,
							PUpGivenMove = 0.5,
							BtcFilterBlocksUp = false,
							BtcFilterBlocksFlat = false,
							BtcFilterBlocksDown = false
							};

						var dayFlat = DayProbabilityBuilder.BuildDayProbabilities (rawFlat);
						var microEmpty = new MicroInfo ();
						var microProbs = ConvertMicro (microEmpty);
						var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

						return new PredResult (1, "day:move-true-dir-missing(flat-fallback)", microEmpty, dayFlat, dayFlatMicro);
						}
					else
						{
						var rawDown = new DailyRawOutput
							{
							PMove = 1.0,
							PUpGivenMove = 0.0,
							BtcFilterBlocksUp = false,
							BtcFilterBlocksFlat = false,
							BtcFilterBlocksDown = false
							};

						var dayDown = DayProbabilityBuilder.BuildDayProbabilities (rawDown);
						var microEmpty = new MicroInfo ();
						var microProbs = ConvertMicro (microEmpty);
						var dayDownMicro = ProbabilityAggregator.ApplyMicroOverlay (dayDown, microProbs, _aggConfig);

						return new PredResult (0, "day:move-true-dir-missing(down-fallback)", microEmpty, dayDown, dayDownMicro);
						}
					}

				var dirEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (dirModel);
				var dirOut = dirEng.Predict (sample);

				bool wantsUp = dirOut.PredictedLabel;

				bool btcBlocksUp = false;
				if (wantsUp)
					{
					bool btcEmaDown = r.BtcEma50vs200 < -0.002;
					bool btcShortRed = r.BtcRet1 < 0 && r.BtcRet30 < 0;

					if (btcEmaDown && btcShortRed)
						{
						btcBlocksUp = true;
						}
					}

				var rawDir = new DailyRawOutput
					{
					PMove = moveOut.Probability,
					PUpGivenMove = dirOut.Probability,
					BtcFilterBlocksUp = btcBlocksUp,
					BtcFilterBlocksFlat = false,
					BtcFilterBlocksDown = false
					};

				var dayProbs = DayProbabilityBuilder.BuildDayProbabilities (rawDir);

				// На направленных днях микро-модель не вызывается → эффект микро 0.
				var microEmptyDir = new MicroInfo ();
				var microProbsDir = ConvertMicro (microEmptyDir);
				var dayWithMicro = ProbabilityAggregator.ApplyMicroOverlay (dayProbs, microProbsDir, _aggConfig);

				if (wantsUp)
					{
					if (btcBlocksUp)
						{
						return new PredResult (1, "day:move-up-blocked-by-btc", microEmptyDir, dayProbs, dayWithMicro);
						}

					return new PredResult (2, "day:move-up", microEmptyDir, dayProbs, dayWithMicro);
					}

				return new PredResult (0, "day:move-down", microEmptyDir, dayProbs, dayWithMicro);
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[PredictionEngine][ERROR] {ex.GetType ().Name}: {ex.Message}");
				Console.WriteLine (ex.StackTrace);
				throw;
				}
			}

		private MicroInfo RunMicroIfAvailable ( DataRow r, float[] fixedFeatures, MLContext ml )
			{
			var microInfo = new MicroInfo ();

			if (_bundle.MicroFlatModel == null)
				return microInfo;

			var microEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MicroFlatModel);

			var microSample = new MlSampleBinary
				{
				Features = fixedFeatures
				};

			var microOut = microEng.Predict (microSample);
			float p = microOut.Probability;

			if (_microDebugPrinted < MicroDebugMaxRows && (r.FactMicroUp || r.FactMicroDown))
				{
				bool accepted = p >= FlatMicroProbThresh;

				Console.WriteLine (
					"[debug-micro] {0:yyyy-MM-dd} factUp={1}, factDown={2}, predUp={3}, prob={4:0.000}, accepted={5}",
					r.Date,
					r.FactMicroUp,
					r.FactMicroDown,
					microOut.PredictedLabel,
					p,
					accepted
				);

				_microDebugPrinted++;
				}

			if (p < FlatMicroProbThresh)
				{
				microInfo.Predicted = false;
				microInfo.Prob = p;
				return microInfo;
				}

			microInfo.Predicted = true;
			microInfo.Up = microOut.PredictedLabel;
			microInfo.ConsiderUp = microOut.PredictedLabel;
			microInfo.ConsiderDown = !microOut.PredictedLabel;
			microInfo.Prob = p;

			if (r.FactMicroUp || r.FactMicroDown)
				{
				microInfo.Correct = microOut.PredictedLabel
					? r.FactMicroUp
					: r.FactMicroDown;
				}

			return microInfo;
			}

		private static MicroProbabilities ConvertMicro ( MicroInfo micro )
			{
			if (micro == null || !micro.Predicted)
				{
				return new MicroProbabilities
					{
					HasPrediction = false,
					PUpGivenFlat = 0.5,
					PDownGivenFlat = 0.5,
					Confidence = 0.0,
					PredLabel = 1
					};
				}

			double pUp = micro.Prob;
			if (pUp < 0.0) pUp = 0.0;
			if (pUp > 1.0) pUp = 1.0;

			double pDown = 1.0 - pUp;

			return new MicroProbabilities
				{
				HasPrediction = true,
				PUpGivenFlat = pUp,
				PDownGivenFlat = pDown,
				Confidence = pUp,
				PredLabel = micro.Up ? 2 : 0
				};
			}

		// EvalMicroAware / EvalWeighted без изменений
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
