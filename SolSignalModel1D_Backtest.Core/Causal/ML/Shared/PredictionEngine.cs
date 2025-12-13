using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Shared
	{
	public sealed class PredictionEngine
		{
		private readonly ModelBundle _bundle;
		private readonly ProbabilityAggregationConfig _aggConfig;
		private readonly MLContext _ml;

		// ML.NET PredictionEngine не потокобезопасен.
		// Этот класс рассчитан на последовательный backtest/analytics-пайплайн.
		private readonly PredictionEngine<MlSampleBinary, MlBinaryOutput>? _moveEng;
		private readonly PredictionEngine<MlSampleBinary, MlBinaryOutput>? _dirNormalEng;
		private readonly PredictionEngine<MlSampleBinary, MlBinaryOutput>? _dirDownEng;
		private readonly PredictionEngine<MlSampleBinary, MlBinaryOutput>? _microEng;

		// Порог принятия микро-сигнала на flat-днях.
		// Лучше бы вынести в конфиг, но это уже отдельная миграция по всему пайплайну.
		private const float FlatMicroProbThresh = 0.60f;

		/// <summary>
		/// Внутренний результат предсказания дневного стека:
		/// - Class — финальный класс (0/1/2);
		/// - Day / DayWithMicro — P_day и P_dayMicro;
		/// - Micro — детальная информация микро-слоя.
		/// </summary>
		private readonly struct PredResult
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
			public DailyProbabilities Day { get; }
			public DailyProbabilities DayWithMicro { get; }
			}

		public PredictionEngine ( ModelBundle bundle, ProbabilityAggregationConfig? aggregationConfig = null )
			{
			_bundle = bundle ?? throw new ArgumentNullException (nameof (bundle));
			_aggConfig = aggregationConfig ?? new ProbabilityAggregationConfig ();

			_ml = _bundle.MlCtx ?? throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MlCtx == null");

			// Жёстко фиксируем зависимость от моделей:
			// - move и dir обязательны для дневного стека (иначе это не «дневной стек», а отладочная заглушка);
			// - micro опционален.
			if (_bundle.MoveModel == null)
				throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MoveModel == null (нет move-модели)");

			if (_bundle.DirModelNormal == null && _bundle.DirModelDown == null)
				throw new InvalidOperationException ("[PredictionEngine] DirModelNormal/DirModelDown == null (нет dir-модели)");

			_moveEng = _ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);

			if (_bundle.DirModelNormal != null)
				_dirNormalEng = _ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.DirModelNormal);

			if (_bundle.DirModelDown != null)
				_dirDownEng = _ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.DirModelDown);

			if (_bundle.MicroFlatModel != null)
				_microEng = _ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MicroFlatModel);
			}

		// =====================================================================
		// PUBLIC CAUSAL API
		// =====================================================================

		/// <summary>
		/// Каузальный API: строит CausalPredictionRecord для одного дня.
		/// ВАЖНО: вход строго causal (CausalDataRow). Здесь запрещены label/forward-ценности/факты.
		/// </summary>
		public CausalPredictionRecord PredictCausal ( CausalDataRow r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));
			var res = PredictInternal (r);
			return ToCausalRecord (r, res);
			}

		/// <summary>
		/// Batch-API для построения каузальных записей по списку дней.
		/// </summary>
		public List<CausalPredictionRecord> PredictManyCausal ( IReadOnlyList<CausalDataRow> rows )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			var result = new List<CausalPredictionRecord> (rows.Count);

			for (int i = 0; i < rows.Count; i++)
				{
				var r = rows[i] ?? throw new InvalidOperationException ("[PredictionEngine] rows contains null CausalDataRow item.");
				var res = PredictInternal (r);
				result.Add (ToCausalRecord (r, res));
				}

			return result;
			}

		/// <summary>
		/// Маппинг внутреннего PredResult + CausalDataRow в каузальный DTO без forward-полей.
		/// </summary>
		private static CausalPredictionRecord ToCausalRecord ( CausalDataRow r, PredResult res )
			{
			var day = res.Day;
			var dayMicro = res.DayWithMicro;
			var micro = res.Micro;

			int predDay = ArgmaxClass (day);
			int predDayMicro = ArgmaxClass (dayMicro);

			return new CausalPredictionRecord
				{
				// as-of момент = дата входа (UTC).
				DateUtc = r.DateUtc,

				// контекст
				RegimeDown = r.RegimeDown,
				MinMove = r.MinMove,
				Reason = res.Reason,

				// финальный класс + вспомогательные Day/DayMicro
				PredLabel = res.Class,
				PredLabel_Day = predDay,
				PredLabel_DayMicro = predDayMicro,

				// распределения P_day
				ProbUp_Day = day.PUp,
				ProbFlat_Day = day.PFlat,
				ProbDown_Day = day.PDown,

				// распределения P_dayMicro
				ProbUp_DayMicro = dayMicro.PUp,
				ProbFlat_DayMicro = dayMicro.PFlat,
				ProbDown_DayMicro = dayMicro.PDown,

				// confidence локально (не завязываемся на реализацию DailyProbabilities)
				Conf_Day = Math.Max (day.PUp, Math.Max (day.PFlat, day.PDown)),
				Conf_Micro = micro.Predicted ? micro.Prob : 0.0,

				// микро-прогноз (без фактов)
				MicroPredicted = micro.Predicted,
				PredMicroUp = micro.Predicted && micro.Up,
				PredMicroDown = micro.Predicted && !micro.Up
				};
			}

		/// <summary>
		/// Аргмакс по классам 0/1/2 из DailyProbabilities:
		/// 0 = down, 1 = flat, 2 = up.
		/// </summary>
		private static int ArgmaxClass ( DailyProbabilities probs )
			{
			double best = probs.PDown;
			int label = 0;

			if (probs.PFlat > best)
				{
				best = probs.PFlat;
				label = 1;
				}

			if (probs.PUp > best)
				{
				label = 2;
				}

			return label;
			}

		// =====================================================================
		// INTERNAL CORE: causal-only inference
		// =====================================================================

		private PredResult PredictInternal ( CausalDataRow r )
			{
			try
				{
				// Конвертация в float[] — без промежуточного double[].
				var fixedFeatures = ToFloatFixed (r.FeaturesVector.Span);

				var sample = new MlSampleBinary { Features = fixedFeatures };

				// ===== 1) move =====
				var moveEng = _moveEng ?? throw new InvalidOperationException ("[PredictionEngine] move engine is not initialized.");
				var moveOut = moveEng.Predict (sample);

				// ===== 2) нет хода -> flat + micro =====
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
					var microInfo = RunMicroIfAvailable (fixedFeatures);
					var microProbs = ConvertMicro (microInfo);
					var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay (dayFlat, microProbs, _aggConfig);

					string reason = microInfo.Predicted
						? microInfo.Up ? "day:flat+microUp" : "day:flat+microDown"
						: "day:flat";

					return new PredResult (1, reason, microInfo, dayFlat, dayFlatMicro);
					}

				// ===== 3) ход есть -> dir =====
				var dirEng = SelectDirEngine (r);
				var dirOut = dirEng.Predict (sample);

				bool wantsUp = dirOut.PredictedLabel;

				bool btcBlocksUp = false;
				if (wantsUp)
					{
					// BTC-фильтр — это часть каузального стека (использует только causal-поля).
					bool btcEmaDown = r.BtcEma50vs200 < -0.002;
					bool btcShortRed = r.BtcRet1 < 0 && r.BtcRet30 < 0;

					if (btcEmaDown && btcShortRed)
						btcBlocksUp = true;
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

				// На направленных днях микро не вызывается (архитектурный выбор).
				var microEmpty = new MicroInfo ();
				var microProbsDir = ConvertMicro (microEmpty);
				var dayWithMicro = ProbabilityAggregator.ApplyMicroOverlay (dayProbs, microProbsDir, _aggConfig);

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

		private PredictionEngine<MlSampleBinary, MlBinaryOutput> SelectDirEngine ( CausalDataRow r )
			{
			// Если есть специализированная down-модель и режим down — используем её.
			// Иначе — normal. Если normal отсутствует, но есть down — используем down как единственную доступную.
			if (r.RegimeDown && _dirDownEng != null)
				return _dirDownEng;

			if (_dirNormalEng != null)
				return _dirNormalEng;

			if (_dirDownEng != null)
				return _dirDownEng;

			throw new InvalidOperationException ("[PredictionEngine] dir engine is not initialized.");
			}

		private MicroInfo RunMicroIfAvailable ( float[] fixedFeatures )
			{
			var microInfo = new MicroInfo ();

			// Микро — опционально.
			if (_microEng == null)
				return microInfo;

			var microSample = new MlSampleBinary { Features = fixedFeatures };
			var microOut = _microEng.Predict (microSample);

			float p = microOut.Probability;

			// Если ниже порога — считаем «нет предсказания»
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
			// Контракт: если HasPrediction=false, аггрегатор обязан игнорировать распределение.
			// Значения 0.5/0.5 оставлены как нейтральные «плейсхолдеры», но они не должны участвовать в расчёте.
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

		private static float[] ToFloatFixed ( ReadOnlySpan<double> features )
			{
			if (features.Length == 0)
				{
				throw new InvalidOperationException (
					"[PredictionEngine] FeaturesVector is empty. " +
					"Это означает, что upstream не собрал фичи для дня.");
				}

			var res = new float[features.Length];

			for (int i = 0; i < features.Length; i++)
				{
				var x = features[i];
				if (double.IsNaN (x) || double.IsInfinity (x))
					{
					throw new InvalidOperationException (
						$"[PredictionEngine] Non-finite feature value at i={i}: {x}.");
					}

				res[i] = (float) x;
				}

			return res;
			}
		}
	}
