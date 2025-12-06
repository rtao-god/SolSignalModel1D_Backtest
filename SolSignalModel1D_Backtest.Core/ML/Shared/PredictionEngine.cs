using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
{
	/// <summary>
	/// Обёртка над натрененным бандлом:
	/// 1) дневной двухшаговый пайплайн (move + dir),
	/// 2) микро-модель, которая срабатывает только при дневном flat.
	/// </summary>
	public sealed class PredictionEngine
	{
		private readonly ModelBundle _bundle;

		/// <summary>
		/// DEBUG-флаг: если true, PredictionEngine не кидает исключения,
		/// когда какая-то из моделей в бандле отсутствует (null),
		/// а использует простые fallback-ветки.
		///
		/// По умолчанию false, т.е. поведение строгое и все модели должны быть включены
		/// Включать только в специальных экспериментах/тестах.
		/// </summary>
		public static bool DebugAllowDisabledModels { get; set; } = false;

		/// <summary>
		/// Если Move-модель отключена (MoveModel == null) и DebugAllowDisabledModels == true:
		/// - при true: считаем, что "хода нет" → всегда дневной flat (class=1);
		/// - при false: считаем, что "ход есть" и сразу идём в dir-модели.
		/// </summary>
		public static bool DebugTreatMissingMoveAsFlat { get; set; } = false;

		/// <summary>
		/// Если dir-модель для нужного режима отсутствует и DebugAllowDisabledModels == true,
		/// и при этом Move говорит "ход есть" — по умолчанию возвращаем flat.
		/// </summary>
		public static bool DebugTreatMissingDirAsFlat { get; set; } = false;

		/// <summary>
		/// Порог уверенности для микро-модели в flat-днях.
		/// </summary>
		private const float FlatMicroProbThresh = 0.60f;

		/// <summary>
		/// Лимит диагностических логов по микро-слою, чтобы не заспамить консоль.
		/// </summary>
		private const int MicroDebugMaxRows = 10;

		/// <summary>
		/// Счётчик уже выведенных диагностических строк по микро-слою.
		/// </summary>
		private static int _microDebugPrinted;

		public readonly struct PredResult
		{
			public PredResult (int cls, string reason, MicroInfo micro)
			{
				Class = cls;
				Reason = reason;
				Micro = micro;
			}

			/// <summary>Итоговый дневной класс: 0=down, 1=flat, 2=up.</summary>
			public int Class { get; }

			/// <summary>Человекочитаемое описание ветки/решения.</summary>
			public string Reason { get; }

			/// <summary>Информация по микро-слою (если он применялся).</summary>
			public MicroInfo Micro { get; }
		}

		public PredictionEngine (ModelBundle bundle)
		{
			_bundle = bundle ?? throw new ArgumentNullException (nameof (bundle));
		}

		/// <summary>
		/// Основной инференс:
		/// 1) move-модель решает, есть ли ход (true/false);
		/// 2) если хода нет → дневной flat (class=1) + опционально микро-слой;
		/// 3) если ход есть → dir-модель даёт направление (0/2) с BTC-фильтром;
		/// 4) при некорректной конфигурации бандла:
		///    - в обычном режиме кидается исключение;
		///    - в debug-режиме с DebugAllowDisabledModels используются fallback-ветки.
		/// </summary>
		public PredResult Predict (DataRow r)
		{
			try
			{
				if (r == null)
					throw new ArgumentNullException (nameof (r));

				if (r.Features == null)
				{
					throw new InvalidOperationException (
						"[PredictionEngine] DataRow.Features == null. " +
						"Проблема в построении DataRow: нет фич для дня " + r.Date.ToString ("O"));
				}

				var fixedFeatures = MlTrainingUtils.ToFloatFixed (r.Features);

				if (_bundle.MlCtx == null)
					throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MlCtx == null (модели не инициализированы)");

				var ml = _bundle.MlCtx;

				// Общий бинарный сэмпл для всех моделей
				var sample = new MlSampleBinary
				{
					Features = fixedFeatures
				};

				// ===== 1. Бинарная модель "есть ли ход" (move) =====

				MlBinaryOutput moveOut;

				if (_bundle.MoveModel == null)
				{
					// В обычном режиме отсутствие move-модели — это ошибка конфигурации.
					if (!DebugAllowDisabledModels)
					{
						throw new InvalidOperationException (
							"[PredictionEngine] ModelBundle.MoveModel == null (нет дневной move-модели)");
					}

					// Debug-режим: моделируем выключенную move-модель.
					if (DebugTreatMissingMoveAsFlat)
					{
						// Вариант 1: считаем, что "хода нет" → сразу flat + микро-слой.
						var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);

						string reason = microInfo.Predicted
							? (microInfo.Up ? "day:flat+microUp(move-disabled)" : "day:flat+microDown(move-disabled)")
							: "day:flat(move-disabled)";

						return new PredResult (1, reason, microInfo);
					}
					else
					{
						// Вариант 2: считаем, что "ход есть", и сразу идём в dir-модели,
						// имитируя "идеальный" move.
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

				// ===== 2. Нет хода → дневной flat + микро-слой (если он есть) =====
				if (!moveOut.PredictedLabel)
				{
					var microInfo = RunMicroIfAvailable (r, fixedFeatures, ml);

					string reason = microInfo.Predicted
						? (microInfo.Up ? "day:flat+microUp" : "day:flat+microDown")
						: "day:flat";

					return new PredResult (1, reason, microInfo);
				}

				// ===== 3. Ход есть → направление (dir) =====

				var dirModel = r.RegimeDown && _bundle.DirModelDown != null
					? _bundle.DirModelDown
					: _bundle.DirModelNormal;

				if (dirModel == null)
				{
					if (!DebugAllowDisabledModels)
					{
						throw new InvalidOperationException (
							"[PredictionEngine] Оба направления (DirModelNormal/DirModelDown) == null — нет дневной dir-модели");
					}

					// Debug-режим: move говорит "ход есть", но dir выключен.
					if (DebugTreatMissingDirAsFlat)
					{
						return new PredResult (1, "day:move-true-dir-missing(flat-fallback)", new MicroInfo ());
					}
					else
					{
						// Альтернативный вариант — всегда down, если нужно для эксперимента.
						return new PredResult (0, "day:move-true-dir-missing(down-fallback)", new MicroInfo ());
					}
				}

				var dirEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (dirModel);
				var dirOut = dirEng.Predict (sample);

				bool wantsUp = dirOut.PredictedLabel;

				if (wantsUp)
				{
					// Фоновый фильтр по BTC: это не заглушка, а реальный risk-filter.
					bool btcEmaDown = r.BtcEma50vs200 < -0.002;
					bool btcShortRed = r.BtcRet1 < 0 && r.BtcRet30 < 0;

					if (btcEmaDown && btcShortRed)
					{
						// BTC-фильтр блокирует лонг → остаёмся во flat.
						return new PredResult (1, "day:move-up-blocked-by-btc", new MicroInfo ());
					}

					return new PredResult (2, "day:move-up", new MicroInfo ());
				}

				return new PredResult (0, "day:move-down", new MicroInfo ());
			}
			catch (Exception ex)
			{
				// Ошибка — редкий случай, логируем, чтобы было понятно, что именно пошло не так.
				Console.WriteLine ($"[PredictionEngine][ERROR] {ex.GetType ().Name}: {ex.Message}");
				Console.WriteLine (ex.StackTrace);
				throw;
			}
		}

		/// <summary>
		/// Микро-слой: вызывается только для дневного flat.
		/// Если модели нет или вероятность ниже порога — возвращается "пустой" MicroInfo.
		/// Никаких подмен класса дня: дневной класс всегда == 1 в этой ветке.
		/// </summary>
		private MicroInfo RunMicroIfAvailable (DataRow r, float[] fixedFeatures, MLContext ml)
		{
			var microInfo = new MicroInfo ();

			if (_bundle.MicroFlatModel == null)
			{
				// Микро-модели нет — дневной flat без уточнения.
				return microInfo;
			}

			var microEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MicroFlatModel);

			var microSample = new MlSampleBinary
			{
				Features = fixedFeatures
			};

			var microOut = microEng.Predict (microSample);
			float p = microOut.Probability;

			// Диагностический лог по реальным микро-дням (есть path-based разметка).
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
				// Вероятность ниже порога — микро-сигнал считаем ненадёжным.
				microInfo.Predicted = false;
				microInfo.Prob = p;
				return microInfo;
			}

			// Достаточно уверенный микро-прогноз
			microInfo.Predicted = true;
			microInfo.Up = microOut.PredictedLabel;
			microInfo.ConsiderUp = microOut.PredictedLabel;
			microInfo.ConsiderDown = !microOut.PredictedLabel;
			microInfo.Prob = p;

			// Заполняем Correct только если есть path-based разметка
			if (r.FactMicroUp || r.FactMicroDown)
			{
				microInfo.Correct = microOut.PredictedLabel
					? r.FactMicroUp
					: r.FactMicroDown;
			}

			return microInfo;
		}

		// ===== Оценка качества с учётом микро-слоя =====

		public bool EvalMicroAware (DataRow r, int predClass, MicroInfo micro)
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

		public double EvalWeighted (DataRow r, int predClass, MicroInfo micro)
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
