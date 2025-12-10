using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.ML;

using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Daily
	{
	/// <summary>
	/// Изолированные тесты для дневного тренера:
	/// проверяем, что move-модель действительно использует поле Label
	/// и качество заметно падает при перетасовке целевой разметки.
	/// </summary>
	public sealed class ModelTrainerIsolationTests
		{
		[Fact]
		public void MoveModel_QualityDrops_WhenTrainLabelsAreShuffled ()
			{
			// 1. Готовим синтетический датасет
			var rows = BuildSyntheticDailyRowsForMove (count: 400);

			// 2. Тренируем "нормальную" модель на правильных лейблах
			var trainerSignal = new ModelTrainer
				{
				// Эти флаги можно включить, чтобы не плодить лишние модели,
				// но для move-теста это не критично.
				DisableDirNormalModel = true,
				DisableDirDownModel = true,
				DisableMicroFlatModel = true
				};

			var bundleSignal = trainerSignal.TrainAll (rows);

			Assert.NotNull (bundleSignal.MoveModel);

			// 3. Тренируем модель на тех же фичах, но с перемешанными лейблами
			var rowsShuffled = CloneRowsWithShuffledLabel (rows, seed: 42);

			var trainerShuffled = new ModelTrainer
				{
				DisableDirNormalModel = true,
				DisableDirDownModel = true,
				DisableMicroFlatModel = true
				};

			var bundleShuffled = trainerShuffled.TrainAll (rowsShuffled);

			Assert.NotNull (bundleShuffled.MoveModel);

			// 4. Объект для оценки — один и тот же eval-датасет с "правильными" таргетами

			var ml = bundleSignal.MlCtx;

			var evalData = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Label != 1,
					 Features = MlTrainingUtils.ToFloatFixed(r.Features)
					}));

			var predSignal = bundleSignal.MoveModel!.Transform (evalData);
			var metricsSignal = ml.BinaryClassification.Evaluate (predSignal);

			var predShuffled = bundleShuffled.MoveModel!.Transform (evalData);
			var metricsShuffled = ml.BinaryClassification.Evaluate (predShuffled);

			// 5. Проверки:
			// - нормальная модель должна учить реальный сигнал
			Assert.True (
				metricsSignal.Accuracy > 0.85,
				$"Expected good accuracy for normal labels, got {metricsSignal.Accuracy:F3}");

			// - модель на шафленных лейблах должна работать заметно хуже
			Assert.True (
				metricsShuffled.Accuracy < metricsSignal.Accuracy - 0.2,
				$"Accuracy with shuffled labels should drop, got {metricsShuffled.Accuracy:F3} vs {metricsSignal.Accuracy:F3}");
			}

		private static List<DataRow> BuildSyntheticDailyRowsForMove ( int count, int seed = 123 )
			{
			var rng = new Random (seed);
			var rows = new List<DataRow> (count);

			// Даты нужны только для каузальной сортировки и фильтрации по trainUntil.
			// Берём будние дни по порядку, чтобы Windowing / DailyDatasetBuilder не спотыкались.
			var start = new DateTime (2020, 1, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var date = start.AddDays (i);

				// Два "сырых" признака в [-1; 1]
				double x1 = rng.NextDouble () * 2.0 - 1.0;
				double x2 = rng.NextDouble () * 2.0 - 1.0;

				// Линейная комбинация + лёгкий шум — истинный "скрытый" скор
				double noise = rng.NextDouble () * 0.2 - 0.1; // [-0.1; 0.1]
				double score = 0.8 * x1 + 0.4 * x2 + noise;

				// hasMove = score > 0 → Label = 2 (up), иначе Label = 1 (flat)
				bool hasMove = score > 0.0;
				int label = hasMove ? 2 : 1;

				var feats = new[]
					{
						x1,
						x2,
						score,           // некий "raw"-скор
						Math.Abs (score) // сила сигнала
						};

				rows.Add (new DataRow
					{
					Date = date,
					Label = label,
					Features = feats,

					// Для move-модели остальные поля роли не играют.
					// Оставляем безопасные значения по умолчанию.
					RegimeDown = false,
					IsMorning = true
					});
				}

			return rows;
			}

		private static List<DataRow> CloneRowsWithShuffledLabel ( List<DataRow> source, int seed = 999 )
			{
			var rng = new Random (seed);

			// Берём все лейблы, перемешиваем отдельно от строк.
			var labels = source
				.Select (r => r.Label)
				.ToList ();

			// Фишер–Йетс
			for (int i = labels.Count - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(labels[i], labels[j]) = (labels[j], labels[i]);
				}

			var result = new List<DataRow> (source.Count);

			for (int i = 0; i < source.Count; i++)
				{
				var src = source[i];

				// Клонируем строку, но подставляем перемешанный Label.
				result.Add (new DataRow
					{
					Date = src.Date,
					Label = labels[i],
					Features = (double[]) src.Features.Clone (),

					RegimeDown = src.RegimeDown,
					IsMorning = src.IsMorning,

					// Остальные поля в этих тестах не используются, 
					// оставляем значения по умолчанию.
					});
				}

			return result;
			}
		}
	}
