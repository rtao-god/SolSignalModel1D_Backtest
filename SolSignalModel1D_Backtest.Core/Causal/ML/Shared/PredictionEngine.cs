using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Shared
	{
	public sealed class PredictionEngine
		{
		private readonly ModelBundle _bundle;
		private readonly ProbabilityAggregationConfig _aggConfig;

		public static bool DebugAllowDisabledModels { get; set; } = false;
		public static bool DebugTreatMissingMoveAsFlat { get; set; } = false;
		public static bool DebugTreatMissingDirAsFlat { get; set; } = false;

		// Порог принятия микро-сигнала по УВЕРЕННОСТИ: max(P(up), P(down)).
		// Важно: это позволяет принимать и Up, и Down; использовать "Probability" напрямую нельзя.
		private const float FlatMicroProbThresh = 0.60f;

		private const int MicroDebugMaxRows = 10;
		private static int _microDebugPrinted;

		public readonly struct PredResult
			{
			public PredResult ( int cls, string reason, MicroInfo micro, DailyProbabilities day, DailyProbabilities dayWithMicro )
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
			public DailyProbabilities Day { get; }
			public DailyProbabilities DayWithMicro { get; }
			}

		public PredictionEngine ( ModelBundle bundle, ProbabilityAggregationConfig? aggregationConfig = null )
			{
			_bundle = bundle ?? throw new ArgumentNullException (nameof (bundle));
			_aggConfig = aggregationConfig ?? new ProbabilityAggregationConfig ();
			}

		public CausalPredictionRecord PredictCausal ( BacktestRecord r )
			{
			var res = Predict (r);
			return ToCausalRecord (r, res);
			}

		public List<CausalPredictionRecord> PredictManyCausal ( IReadOnlyList<BacktestRecord> rows )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			var result = new List<CausalPredictionRecord> (rows.Count);

			for (int i = 0; i < rows.Count; i++)
				{
				var r = rows[i] ?? throw new InvalidOperationException ("[PredictionEngine] rows contains null BacktestRecord item.");

				var res = Predict (r);
				result.Add (ToCausalRecord (r, res));
				}

			return result;
			}

		private static CausalPredictionRecord ToCausalRecord ( BacktestRecord r, PredResult res )
			{
			var day = res.Day;
			var dayMicro = res.DayWithMicro;
			var micro = res.Micro;

			int predDay = ArgmaxClass (day);
			int predDayMicro = ArgmaxClass (dayMicro);

			double confDay = Math.Max (day.PUp, Math.Max (day.PFlat, day.PDown));

			// Конфиденс микро должен быть симметричным: уверенный DOWN == высокая уверенность.
			// micro.Prob хранит P(up); уверенность = max(P(up), 1-P(up)).
			double confMicro = micro.Predicted ? Math.Max (micro.Prob, 1.0 - micro.Prob) : 0.0;

			return new CausalPredictionRecord
				{
				DateUtc = r.ToCausalDateUtc (),

				// Важно сохранять фичи/контекст, иначе downstream-диагностика теряет входные данные.
				Features = r.Causal.Features,

				RegimeDown = r.RegimeDown,
				MinMove = r.MinMove,
				Reason = res.Reason,

				PredLabel = res.Class,
				PredLabel_Day = predDay,
				PredLabel_DayMicro = predDayMicro,

				// Total на этом этапе = Day+Micro (SL/Delayed могут обновить позже).
				PredLabel_Total = predDayMicro,

				ProbUp_Day = day.PUp,
				ProbFlat_Day = day.PFlat,
				ProbDown_Day = day.PDown,

				ProbUp_DayMicro = dayMicro.PUp,
				ProbFlat_DayMicro = dayMicro.PFlat,
				ProbDown_DayMicro = dayMicro.PDown,

				ProbUp_Total = dayMicro.PUp,
				ProbFlat_Total = dayMicro.PFlat,
				ProbDown_Total = dayMicro.PDown,

				Conf_Day = confDay,
				Conf_Micro = confMicro,

				MicroPredicted = micro.Predicted,
				PredMicroUp = micro.Predicted && micro.Up,
				PredMicroDown = micro.Predicted && !micro.Up,

				// SL-слой не выставляется заглушками: null = не считали/не применимо.
				SlProb = null,
				SlHighDecision = null,
				Conf_SlLong = null,
				Conf_SlShort = null,

				// Delayed-слой: null = не считали/не применимо.
				DelayedSource = null,
				DelayedEntryAsked = null,
				DelayedEntryUsed = null,
				DelayedIntradayTpPct = null,
				DelayedIntradaySlPct = null,
				TargetLevelClass = null
				};
			}

		private static int ArgmaxClass ( DailyProbabilities probs )
			{
			double best = probs.PDown;
			int label = 0;

			if (probs.PFlat > best) { best = probs.PFlat; label = 1; }
			if (probs.PUp > best) { label = 2; }

			return label;
			}

		public PredResult Predict ( BacktestRecord r )
			{
			try
				{
				if (r == null)
					throw new ArgumentNullException (nameof (r));

				if (r.Causal.Features == null)
					{
					throw new InvalidOperationException (
						"[PredictionEngine] BacktestRecord.Causal.Features == null. Нет фич для дня " +
						r.ToCausalDateUtc ().ToString ("O"));
					}

				var fixedFeatures = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector);

				if (_bundle.MlCtx == null)
					throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MlCtx == null");

				var ml = _bundle.MlCtx;

				var sample = new MlSampleBinary { Features = fixedFeatures };

				// ===== 1) Move =====
				MlBinaryOutput moveOut;

				if (_bundle.MoveModel == null)
					{
					if (!DebugAllowDisabledModels)
						throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MoveModel == null (нет move-модели)");

					if (!DebugTreatMissingMoveAsFlat)
						{
						// Запрещено “додумывать” поведение молча. Если разрешён дебаг-режим,
						// то fallback должен быть явным флагом.
						throw new InvalidOperationException (
							"[PredictionEngine] MoveModel is missing. " +
							"Enable DebugTreatMissingMoveAsFlat explicitly if you really want flat-fallback.");
						}

					var dayFlat = BuildPureFlatDayProbabilities ();
					var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);
					var microProbs = ConvertMicro (microInfo);
					var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

					string reason = microInfo.Predicted
						? microInfo.Up ? "day:flat+microUp(move-missing)" : "day:flat+microDown(move-missing)"
						: "day:flat(move-missing)";

					return new PredResult (1, reason, microInfo, dayFlat, dayFlatMicro);
					}
				else
					{
					var moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);
					moveOut = moveEng.Predict (sample);
					}

				// В ML.NET Probability обычно означает P(positive-class). Здесь positive = "move".
				double pMove = moveOut.Probability;

				if (!double.IsFinite (pMove) || pMove < 0.0 || pMove > 1.0)
					{
					throw new InvalidOperationException (
						$"[PredictionEngine] invalid move probability: {pMove}. Expected finite value in [0..1].");
					}

				// ===== 2) Dir (считаем ВСЕГДА при наличии модели) =====
				var dirModel = r.RegimeDown && _bundle.DirModelDown != null
					? _bundle.DirModelDown
					: _bundle.DirModelNormal;

				if (dirModel == null)
					{
					if (!DebugAllowDisabledModels)
						throw new InvalidOperationException ("[PredictionEngine] DirModelNormal/DirModelDown == null (нет dir-модели)");

					if (!DebugTreatMissingDirAsFlat)
						{
						throw new InvalidOperationException (
							"[PredictionEngine] DirModel is missing. " +
							"Enable DebugTreatMissingDirAsFlat explicitly if you really want flat-fallback.");
						}

					// Явный fallback: без dir-модели нельзя получить P(up|move), поэтому день считаем flat.
					var dayFlat = BuildPureFlatDayProbabilities ();
					var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);
					var microProbs = ConvertMicro (microInfo);
					var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

					string reason = microInfo.Predicted
						? microInfo.Up ? "day:flat+microUp(dir-missing)" : "day:flat+microDown(dir-missing)"
						: "day:flat(dir-missing)";

					return new PredResult (1, reason, microInfo, dayFlat, dayFlatMicro);
					}

				var dirEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (dirModel);
				var dirOut = dirEng.Predict (sample);

				// В ML.NET Probability обычно означает P(positive-class). Здесь positive = "up".
				double pUpGivenMove = dirOut.Probability;

				if (!double.IsFinite (pUpGivenMove) || pUpGivenMove < 0.0 || pUpGivenMove > 1.0)
					{
					throw new InvalidOperationException (
						$"[PredictionEngine] invalid dir probability: {pUpGivenMove}. Expected finite value in [0..1].");
					}

				bool wantsUp = dirOut.PredictedLabel;

				// ===== 3) BTC-фильтр (как было) =====
				bool btcBlocksUp = false;
				if (wantsUp)
					{
					bool btcEmaDown = r.Causal.BtcEma50vs200 < -0.002;
					bool btcShortRed = r.Causal.BtcRet1 < 0 && r.Causal.BtcRet30 < 0;

					if (btcEmaDown && btcShortRed)
						btcBlocksUp = true;
					}

				// ===== 4) Day distribution без заглушек: P(move) + P(up|move) =====
				var rawDir = new DailyRawOutput
					{
					PMove = pMove,
					PUpGivenMove = pUpGivenMove,
					BtcFilterBlocksUp = btcBlocksUp,
					BtcFilterBlocksFlat = false,
					BtcFilterBlocksDown = false
					};

				var dayProbs = DayProbabilityBuilder.BuildDayProbabilities (rawDir);

				// ===== 5) Flat-ветка: микро допускается только если move сказал "flat" =====
				if (!moveOut.PredictedLabel)
					{
					var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);
					var microProbs = ConvertMicro (microInfo);
					var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayProbs, microProbs, _aggConfig);

					string reason = microInfo.Predicted
						? microInfo.Up ? "day:flat+microUp" : "day:flat+microDown"
						: "day:flat";

					// Важно: поведение сохраняется как раньше — итоговый класс для flat-ветки = 1,
					// а Day+Micro живёт отдельно для аналитики/оверлеев.
					return new PredResult (1, reason, microInfo, dayProbs, dayFlatMicro);
					}

				// ===== 6) Move=true: микро не применяется =====
				var microEmpty = new MicroInfo ();
				var microProbsEmpty = ConvertMicro (microEmpty);
				var dayWithMicro = ProbabilityAggregator.ApplyMicroOverlay (dayProbs, microProbsEmpty, _aggConfig);

				if (wantsUp)
					{
					if (btcBlocksUp)
						return new PredResult (1, "day:move-up-blocked-by-btc", microEmpty, dayProbs, dayWithMicro);

					return new PredResult (2, "day:move-up", microEmpty, dayProbs, dayWithMicro);
					}

				return new PredResult (0, "day:move-down", microEmpty, dayProbs, dayWithMicro);
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[PredictionEngine][ERROR] {ex.GetType ().Name}: {ex.Message}");
				Console.WriteLine (ex.StackTrace);
				throw;
				}
			}

		private MicroInfo RunMicroIfAvailable ( BacktestRecord r, float[] fixedFeatures, MLContext ml )
			{
			var microInfo = new MicroInfo ();

			if (_bundle.MicroFlatModel == null)
				return microInfo;

			var microEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MicroFlatModel);

			var microSample = new MlSampleBinary { Features = fixedFeatures };

			var microOut = microEng.Predict (microSample);

			// В ML.NET Probability = P(positive-class). Здесь positive = "up".
			double pUp = microOut.Probability;

			if (!double.IsFinite (pUp) || pUp < 0.0 || pUp > 1.0)
				{
				throw new InvalidOperationException (
					$"[micro] invalid probability from model: Prob={pUp}. Expected finite value in [0..1].");
				}

			double pDown = 1.0 - pUp;
			double confidence = Math.Max (pUp, pDown);
			bool accepted = confidence >= FlatMicroProbThresh;

			// Для удобства дебага печатаем направление и уверенность, а не сырую "Probability" как критерий принятия.
			if (_microDebugPrinted < MicroDebugMaxRows && (r.FactMicroUp || r.FactMicroDown))
				{
				bool up = pUp >= 0.5;

				Console.WriteLine (
					"[debug-micro] {0:yyyy-MM-dd} factUp={1}, factDown={2}, dir={3}, pUp={4:0.000}, conf={5:0.000}, accepted={6}",
					r.ToCausalDateUtc (),
					r.FactMicroUp,
					r.FactMicroDown,
					up ? "UP" : "DOWN",
					pUp,
					confidence,
					accepted
				);

				_microDebugPrinted++;
				}

			if (!accepted)
				{
				// Модель запускалась, но сигнал не принят: это НЕ прогноз.
				// Prob сохраняем как P(up) для диагностики.
				microInfo.Predicted = false;
				microInfo.Prob = (float) pUp;
				return microInfo;
				}

			bool microUp = pUp >= 0.5;

			microInfo.Predicted = true;
			microInfo.Up = microUp;
			microInfo.ConsiderUp = microUp;
			microInfo.ConsiderDown = !microUp;

			// Контракт: Prob хранит P(up). Down-вариант = 1-Prob.
			microInfo.Prob = (float) pUp;

			if (r.FactMicroUp || r.FactMicroDown)
				{
				microInfo.Correct = microUp ? r.FactMicroUp : r.FactMicroDown;
				}

			return microInfo;
			}

		private static MicroProbabilities ConvertMicro ( MicroInfo micro )
			{
			// Инвариант: даже “непринятый” результат модели обязан быть валидным числом [0..1],
			// иначе это поломка ML-пайплайна, которую нельзя маскировать clamp'ами.
			double pUp = micro.Prob;

			if (!double.IsFinite (pUp) || pUp < 0.0 || pUp > 1.0)
				{
				throw new InvalidOperationException (
					$"[micro] invalid probability value: Prob={pUp}. Expected finite value in [0..1].");
				}

			if (!micro.Predicted)
				{
				// Контракт: отсутствие микро-прогноза не должно “притворяться” вероятностями.
				// NaN гарантирует, что любой код, который полезет в micro без проверки HasPrediction, упадёт сразу.
				return new MicroProbabilities
					{
					HasPrediction = false,
					PUpGivenFlat = double.NaN,
					PDownGivenFlat = double.NaN,
					Confidence = 0.0,
					PredLabel = 1
					};
				}

			double pDown = 1.0 - pUp;

			return new MicroProbabilities
				{
				HasPrediction = true,
				PUpGivenFlat = pUp,
				PDownGivenFlat = pDown,

				// Уверенность микро должна быть симметричной (up/down).
				Confidence = Math.Max (pUp, pDown),

				PredLabel = micro.Up ? 2 : 0
				};
			}

		private static DailyProbabilities BuildPureFlatDayProbabilities ()
			{
			// Явная константа “flat=1.0” — это не заглушка, а корректное распределение,
			// используемое только в ситуациях, когда модель отсутствует и выбран явный debug-fallback.
			return new DailyProbabilities
				{
				PUp = 0.0,
				PFlat = 1.0,
				PDown = 0.0,
				Confidence = 1.0,
				BtcFilterBlockedUp = false,
				BtcFilterBlockedFlat = false,
				BtcFilterBlockedDown = false
				};
			}

		public bool EvalMicroAware ( BacktestRecord r, int predClass, MicroInfo micro )
			{
			bool baseCorrect = predClass == r.Forward.TrueLabel;
			if (baseCorrect) return true;

			if (r.Forward.TrueLabel == 2 && predClass == 1 && micro.ConsiderUp) return true;
			if (r.Forward.TrueLabel == 0 && predClass == 1 && micro.ConsiderDown) return true;
			if (r.Forward.TrueLabel == 1 && r.FactMicroUp && predClass == 2) return true;
			if (r.Forward.TrueLabel == 1 && r.FactMicroDown && predClass == 0) return true;
			if (r.Forward.TrueLabel == 1 && r.FactMicroUp && predClass == 1 && micro.ConsiderUp) return true;
			if (r.Forward.TrueLabel == 1 && r.FactMicroDown && predClass == 1 && micro.ConsiderDown) return true;

			return false;
			}

		public double EvalWeighted ( BacktestRecord r, int predClass, MicroInfo micro )
			{
			int fact = r.Forward.TrueLabel;

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
