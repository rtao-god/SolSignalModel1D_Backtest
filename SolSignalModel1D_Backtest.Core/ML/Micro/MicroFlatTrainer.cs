using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Shared;
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

			if (flats.Count < 30)
				{
				Console.WriteLine ($"[2stage-micro] мало микро-дней ({flats.Count}), скипаем обучение микро-слоя");
				return null;
				}

			// Жёсткая балансировка up/down: берём одинаковое количество.
			var up = flats.Where (r => r.FactMicroUp).ToList ();
			var dn = flats.Where (r => r.FactMicroDown).ToList ();
			int take = Math.Min (up.Count, dn.Count);

			if (take > 0)
				{
				up = up
					.Take (take)
					.OrderBy (r => r.Date)
					.ToList ();

				dn = dn
					.Take (take)
					.OrderBy (r => r.Date)
					.ToList ();

				flats = up
					.Concat (dn)
					.OrderBy (r => r.Date)
					.ToList ();
				}

			var data = ml.Data.LoadFromEnumerable (
				flats.Select (r => new MlSampleBinary
					{
					Label = r.FactMicroUp, // true => microUp, false => microDown
					Features = MlTrainingUtils.ToFloatFixed (r.Features)
					})
			);

			var pipe = ml.BinaryClassification.Trainers.LightGbm (
				new LightGbmBinaryTrainer.Options
					{
					NumberOfLeaves = 12,
					NumberOfIterations = 70,
					LearningRate = 0.07f,
					MinimumExampleCountPerLeaf = 15,
					Seed = 42,
					NumberOfThreads = 1
					});

			var model = pipe.Fit (data);
			Console.WriteLine ($"[2stage-micro] обучено на {flats.Count} REAL микро-днях");
			return model;
			}
		}
	}
