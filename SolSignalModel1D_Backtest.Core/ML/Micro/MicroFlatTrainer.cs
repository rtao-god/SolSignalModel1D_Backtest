using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.ML.Micro
	{
	/// <summary>
	/// Тренер микро-модели для боковика.
	/// ВАЖНО:
	/// - микро-слой опционален: если размеченных микро-дней мало, он просто не строится (возвращается null);
	/// - любые реальные проблемы с датасетом при нормальном объёме истории приводят к InvalidOperationException.
	/// </summary>
	public static class MicroFlatTrainer
		{
		/// <summary>
		/// Минимальное число размеченных микро-дней, при котором имеет смысл учить LightGBM.
		/// Меньше этого порога — считаем, что история ещё «слишком молодая» для микро-слоя.
		/// </summary>
		private const int MinMicroRowsForTraining = 40;

		/// <summary>
		/// Строит микро-модель для flat-дней.
		/// Возвращает null, если микро-датасета недостаточно для осмысленного обучения.
		/// При нормальном объёме (flats >= MinMicroRowsForTraining) любые проблемы с данными (NaN/Inf,
		/// одноклассовость, скачущая размерность, падение LightGBM) приводят к InvalidOperationException.
		/// </summary>
		public static ITransformer? BuildMicroFlatModel ( MLContext ml, List<DataRow> rows )
			{
			if (ml == null) throw new ArgumentNullException (nameof (ml));
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			// 1. Сырой датасет микро-дней (есть FactMicroUp/FactMicroDown).
			var flatsRaw = rows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderBy (r => r.Date)
				.ToList ();

			// Нет ни одного размеченного микро-дня — это не ошибка, просто микро-слоя быть не может.
			if (flatsRaw.Count == 0)
				{
				Console.WriteLine ("[2stage-micro] нет ни одного размеченного микро-дня, микро-слой отключён.");
				return null;
				}

			// История микро-дней есть, но её объективно мало.
			// Учить LightGBM на 5–10 строках бессмысленно: получится шум и нестабильность.
			if (flatsRaw.Count < MinMicroRowsForTraining)
				{
				Console.WriteLine (
					$"[2stage-micro] датасет микро-дней слишком мал (flats={flatsRaw.Count}, " +
					$"min={MinMicroRowsForTraining}), микро-слой отключён для этого прогона."
				);
				return null;
				}

			// 2. Балансируем up/down.
			var up = flatsRaw.Where (r => r.FactMicroUp).ToList ();
			var dn = flatsRaw.Where (r => r.FactMicroDown).ToList ();

			// На «взрослом» датасете отсутствие одного из классов — уже реальная проблема разметки.
			if (up.Count == 0 || dn.Count == 0)
				{
				throw new InvalidOperationException (
					$"[2stage-micro] датасет микро-дней одноклассовый (up={up.Count}, down={dn.Count}) " +
					$"при flats={flatsRaw.Count}. Проверь path-based разметку FactMicroUp/FactMicroDown."
				);
				}

			int take = Math.Min (up.Count, dn.Count);

			var upBalanced = up
				.OrderBy (r => r.Date)
				.Take (take)
				.ToList ();

			var dnBalanced = dn
				.OrderBy (r => r.Date)
				.Take (take)
				.ToList ();

			var flats = upBalanced
				.Concat (dnBalanced)
				.OrderBy (r => r.Date)
				.ToList ();

			// 3. Жёсткая валидация признаков перед LightGBM.
			var samples = new List<MlSampleBinary> (flats.Count);
			int? featureDim = null;
			bool hasNaN = false;
			bool hasInf = false;

			foreach (var r in flats)
				{
				var feats = MlTrainingUtils.ToFloatFixed (r.Features);

				if (feats == null)
					{
					throw new InvalidOperationException (
						"[2stage-micro] ToFloatFixed вернул null для вектора признаков микро-слоя."
					);
					}

				if (featureDim == null)
					{
					featureDim = feats.Length;
					if (featureDim <= 0)
						{
						throw new InvalidOperationException (
							"[2stage-micro] длина вектора признаков для микро-слоя равна 0."
						);
						}
					}
				else if (feats.Length != featureDim.Value)
					{
					throw new InvalidOperationException (
						$"[2stage-micro] неконсистентная длина признаков: ожидалось {featureDim.Value}, " +
						$"получено {feats.Length}."
					);
					}

				for (int i = 0; i < feats.Length; i++)
					{
					if (float.IsNaN (feats[i])) hasNaN = true;
					else if (float.IsInfinity (feats[i])) hasInf = true;
					}

				samples.Add (new MlSampleBinary
					{
					// true => microUp, false => microDown
					Label = r.FactMicroUp,
					Features = feats
					});
				}

			if (hasNaN || hasInf)
				{
				throw new InvalidOperationException (
					$"[2stage-micro] датасет микро-слоя содержит некорректные значения признаков " +
					$"(NaN={hasNaN}, Inf={hasInf})."
				);
				}

			var data = ml.Data.LoadFromEnumerable (samples);

			var options = new LightGbmBinaryTrainer.Options
				{
				NumberOfLeaves = 12,
				NumberOfIterations = 70,
				LearningRate = 0.07f,
				MinimumExampleCountPerLeaf = 15,
				Seed = 42,
				NumberOfThreads = 1
				};

			try
				{
				var pipe = ml.BinaryClassification.Trainers.LightGbm (options);
				var model = pipe.Fit (data);

				Console.WriteLine (
					$"[2stage-micro] обучено на {flats.Count} REAL микро-днях " +
					$"(up={upBalanced.Count}, down={dnBalanced.Count}, featDim={featureDim})"
				);

				return model;
				}
			catch (Exception ex)
				{
				// Сюда должны попадать только реальные сбои LightGBM при уже проверенном датасете.
				throw new InvalidOperationException (
					"[2stage-micro] LightGBM не смог обучить микро-модель при корректном датасете. " +
					$"flats={flats.Count}, up={upBalanced.Count}, down={dnBalanced.Count}, " +
					$"featDim={featureDim ?? -1}. См. InnerException.",
					ex
				);
				}
			}
		}
	}
