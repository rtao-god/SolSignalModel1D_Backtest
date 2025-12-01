using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.ML.Micro
	{
	/// <summary>
	/// Тренер микро-модели для боковика.
	/// Берёт только дни, где размечен факт микро-направления (FactMicroUp/FactMicroDown),
	/// балансирует up/down и обучает бинарный LightGBM.
	/// Никаких внутренних "угадываний" train/test: утечка контролируется снаружи выбором trainRows.
	/// </summary>
	public static class MicroFlatTrainer
		{
		/// <summary>
		/// Строит микро-модель для flat-дней.
		/// Возвращает null, если размеченных микро-дней слишком мало (меньше 30).
		/// </summary>
		public static ITransformer? BuildMicroFlatModel ( MLContext ml, List<DataRow> rows )
			{
			if (ml == null) throw new ArgumentNullException (nameof (ml));
			if (rows == null) throw new ArgumentNullException (nameof (rows));

			// Берём только дни, где есть path-based micro ground truth.
			var flats = rows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.OrderBy (r => r.Date)
				.ToList ();

			// Это нормальная ситуация на сырой истории: микро-датасет ещё не набрался.
			// Здесь не считаем это «проблемой пайплайна», просто микро-слой не строится.
			if (flats.Count < 30)
				{
				Console.WriteLine ($"[2stage-micro] мало микро-дней ({flats.Count}), скипаем обучение микро-слоя");
				return null;
				}

			// Балансируем up/down.
			var up = flats.Where (r => r.FactMicroUp).ToList ();
			var dn = flats.Where (r => r.FactMicroDown).ToList ();

			// А вот одноклассовый датасет — это уже реальная проблема (разметка / RowBuilder).
			if (up.Count == 0 || dn.Count == 0)
				{
				throw new InvalidOperationException (
					$"[2stage-micro] датасет микро-дней одноклассовый (up={up.Count}, down={dn.Count}). " +
					"Проверь разметку FactMicroUp/FactMicroDown и path-based labeling."
				);
				}

			int take = Math.Min (up.Count, dn.Count);

			up = up
				.OrderBy (r => r.Date)
				.Take (take)
				.ToList ();

			dn = dn
				.OrderBy (r => r.Date)
				.Take (take)
				.ToList ();

			flats = up
				.Concat (dn)
				.OrderBy (r => r.Date)
				.ToList ();

			// === ЖЁСТКАЯ ВАЛИДАЦИЯ ФИЧЕЙ ПЕРЕД LightGBM ===

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
						"[2stage-micro] ToFloatFixed вернул null-массив признаков для микро-дня."
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
					Label = r.FactMicroUp, // true => microUp, false => microDown
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
					$"(up={up.Count}, down={dn.Count}, featDim={featureDim})"
				);

				return model;
				}
			catch (Exception ex)
				{
				// Любой сбой LightGBM — это уже «проблема» и пробрасывается наверх
				// с максимальным количеством контекста.
				throw new InvalidOperationException (
					"[2stage-micro] LightGBM не смог обучить микро-модель. " +
					$"flats={flats.Count}, up={up.Count}, down={dn.Count}, featDim={featureDim ?? -1}. " +
					"См. InnerException для деталей.",
					ex
				);
				}
			}
		}
	}
