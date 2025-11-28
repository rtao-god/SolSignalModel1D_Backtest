using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.ML
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
		/// Порог уверенности для микро-модели в flat-днях.
		/// </summary>
		private const float FlatMicroProbThresh = 0.60f;

		public readonly struct PredResult
			{
			public PredResult ( int cls, string reason, MicroInfo micro )
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

		public PredictionEngine ( ModelBundle bundle )
			{
			_bundle = bundle ?? throw new ArgumentNullException (nameof (bundle));
			}

		/// <summary>
		/// Основной инференс:
		/// 1) move-модель решает, есть ли ход (true/false);
		/// 2) если хода нет → дневной flat (class=1) + опционально микро-слой;
		/// 3) если ход есть → dir-модель даёт направление (0/2) с BTC-фильтром;
		/// 4) никаких fallback-веток: при некорректной конфигурации бандла — исключение.
		/// </summary>
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
						"Проблема в построении DataRow: нет фич для дня " + r.Date.ToString ("O"));
					}

				var fixedFeatures = MlTrainingUtils.ToFloatFixed (r.Features);

				if (_bundle.MlCtx == null)
					throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MlCtx == null (модели не инициализированы)");

				var ml = _bundle.MlCtx;

				if (_bundle.MoveModel == null)
					throw new InvalidOperationException ("[PredictionEngine] ModelBundle.MoveModel == null (нет дневной move-модели)");

				if (_bundle.DirModelNormal == null && _bundle.DirModelDown == null)
					throw new InvalidOperationException (
						"[PredictionEngine] Оба направления (DirModelNormal/DirModelDown) == null — нет дневной dir-модели");

				// Общий бинарный сэмпл для всех моделей
				var sample = new MlSampleBinary
					{
					Features = fixedFeatures
					};

				// ===== 1. Бинарная модель "есть ли ход" (move) =====
				var moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput> (_bundle.MoveModel);
				var moveOut = moveEng.Predict (sample);

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
					throw new InvalidOperationException (
						"[PredictionEngine] Нет dir-модели ни для нормального, ни для даун-режима");
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
		private MicroInfo RunMicroIfAvailable ( DataRow r, float[] fixedFeatures, MLContext ml )
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

		// ===== Оценка качества с учётом микро-слоя (оставляю как было) =====

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
