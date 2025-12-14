using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Shared
	{
	/// <summary>
	/// Каузальный inference-движок дневного стека (move+dir+micro).
	/// Важно:
	/// - на вход принимает ТОЛЬКО CausalDataRow;
	/// - на выход отдаёт CausalPredictionRecord без true label;
	/// - оценка качества делается только в omniscient-слое.
	/// </summary>
	public sealed class PredictionEngine
		{
		private readonly ModelBundle _bundle;
		private readonly ProbabilityAggregationConfig _aggConfig;

		private readonly Microsoft.ML.PredictionEngine<MlSampleBinary, MlBinaryOutput> _moveEng;
		private readonly Microsoft.ML.PredictionEngine<MlSampleBinary, MlBinaryOutput> _dirNormalEng;
		private readonly Microsoft.ML.PredictionEngine<MlSampleBinary, MlBinaryOutput>? _dirDownEng;
		private readonly Microsoft.ML.PredictionEngine<MlSampleBinary, MlBinaryOutput>? _microFlatEng;

		private const float FlatMicroProbThresh = 0.60f;

		public PredictionEngine ( ModelBundle bundle, ProbabilityAggregationConfig? aggregationConfig = null )
			{
			_bundle = bundle ?? throw new ArgumentNullException (nameof (bundle));
			_aggConfig = aggregationConfig ?? new ProbabilityAggregationConfig ();

			if (_bundle.MlCtx == null)
				throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MlCtx == null");

			if (_bundle.MoveModel == null)
				throw new InvalidOperationException ("[PredictionEngine] MoveModel is null. Это не допустимо для продового стека.");

			if (_bundle.DirModelNormal == null)
				throw new InvalidOperationException ("[PredictionEngine] DirModelNormal is null. Это не допустимо для продового стека.");

			var ml = _bundle.MlCtx;

			_moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);
			_dirNormalEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.DirModelNormal);

			_dirDownEng = _bundle.DirModelDown != null
				? ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.DirModelDown)
				: null;

			_microFlatEng = _bundle.MicroFlatModel != null
				? ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MicroFlatModel)
				: null;
			}

		public CausalPredictionRecord PredictCausal ( CausalDataRow r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			var fixedFeatures = MlTrainingUtils.ToFloatFixed (r.FeaturesVector);
			var sample = new MlSampleBinary { Features = fixedFeatures };

			// ===== 1) move =====
			var moveOut = _moveEng.Predict (sample);

			// ===== 2) no-move -> flat + micro overlay (если micro доступен) =====
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

				var micro = RunMicroIfAvailable (fixedFeatures);
				var microProbs = ConvertMicro (micro);
				var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

				string reason = micro.Predicted
					? (micro.Up ? "day:flat+microUp" : "day:flat+microDown")
					: "day:flat";

				return BuildCausalRecord (
					r,
					reason,
					finalClass: 1,
					day: dayFlat,
					dayWithMicro: dayFlatMicro,
					micro: micro);
				}

			// ===== 3) move -> dir =====
			var dirEng = (r.RegimeDown && _dirDownEng != null) ? _dirDownEng : _dirNormalEng;
			var dirOut = dirEng.Predict (sample);

			bool wantsUp = dirOut.PredictedLabel;

			// BTC-фильтр ап-дней (rule-based, вне ML-вектора).
			bool btcBlocksUp = false;
			if (wantsUp)
				{
				bool btcEmaDown = r.Causal.BtcEma50vs200 < -0.002;
				bool btcShortRed = r.Causal.BtcRet1 < 0 && r.Causal.BtcRet30 < 0;
				if (btcEmaDown && btcShortRed) btcBlocksUp = true;
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

			// На направленных днях micro не вызываем => overlay нейтральный.
			var microEmpty = new MicroInfo ();
			var microProbsDir = ConvertMicro (microEmpty);
			var dayWithMicro = ProbabilityAggregator.ApplyMicroOverlay (dayProbs, microProbsDir, _aggConfig);

			if (wantsUp)
				{
				if (btcBlocksUp)
					return BuildCausalRecord (r, "day:move-up-blocked-by-btc", 1, dayProbs, dayWithMicro, microEmpty);

				return BuildCausalRecord (r, "day:move-up", 2, dayProbs, dayWithMicro, microEmpty);
				}

			return BuildCausalRecord (r, "day:move-down", 0, dayProbs, dayWithMicro, microEmpty);
			}

		private static CausalPredictionRecord BuildCausalRecord (
			CausalDataRow r,
			string reason,
			int finalClass,
			DailyProbabilities day,
			DailyProbabilities dayWithMicro,
			MicroInfo micro )
			{
			static int ArgmaxClass ( DailyProbabilities probs )
				{
				double best = probs.PDown;
				int label = 0;

				if (probs.PFlat > best) { best = probs.PFlat; label = 1; }
				if (probs.PUp > best) { label = 2; }

				return label;
				}

			int predDay = ArgmaxClass (day);
			int predDayMicro = ArgmaxClass (dayWithMicro);

			return new CausalPredictionRecord
				{
				DateUtc = r.DateUtc,

				PredLabel = finalClass,
				PredLabel_Day = predDay,
				PredLabel_DayMicro = predDayMicro,
				PredLabel_Total = finalClass,

				ProbUp_Day = day.PUp,
				ProbFlat_Day = day.PFlat,
				ProbDown_Day = day.PDown,

				ProbUp_DayMicro = dayWithMicro.PUp,
				ProbFlat_DayMicro = dayWithMicro.PFlat,
				ProbDown_DayMicro = dayWithMicro.PDown,

				ProbUp_Total = dayWithMicro.PUp,
				ProbFlat_Total = dayWithMicro.PFlat,
				ProbDown_Total = dayWithMicro.PDown,

				Conf_Day = Math.Max (day.PUp, Math.Max (day.PFlat, day.PDown)),
				Conf_Micro = micro.Predicted ? micro.Prob : 0.0,

				MicroPredicted = micro.Predicted,
				PredMicroUp = micro.Predicted && micro.Up,
				PredMicroDown = micro.Predicted && !micro.Up,

				RegimeDown = r.RegimeDown,
				MinMove = r.MinMove,
				Reason = reason,

				SlProb = 0.0,
				SlHighDecision = false,
				Conf_SlLong = 0.0,
				Conf_SlShort = 0.0,

				DelayedSource = null,
				DelayedEntryAsked = false,
				DelayedEntryUsed = false,
				DelayedIntradayTpPct = 0.0,
				DelayedIntradaySlPct = 0.0,
				TargetLevelClass = 0
				};
			}

		private MicroInfo RunMicroIfAvailable ( float[] fixedFeatures )
			{
			var microInfo = new MicroInfo ();

			if (_microFlatEng == null)
				return microInfo;

			var microSample = new MlSampleBinary { Features = fixedFeatures };
			var microOut = _microFlatEng.Predict (microSample);

			float p = microOut.Probability;

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

			return microInfo;
			}

		private static MicroProbabilities ConvertMicro ( MicroInfo micro )
			{
			if (!micro.Predicted)
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
		}
	}
