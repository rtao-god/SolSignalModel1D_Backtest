using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Micro
	{
	/// <summary>
	/// Изолированные тесты для MicroFlatTrainer:
	/// проверяем порог по количеству микро-дней,
	/// защиту от одноклассового датасета и базовую адекватность обучения.
	/// </summary>
	public sealed class MicroFlatTrainerIsolationTests
		{
		[Fact]
		public void BuildMicroFlatModel_ReturnsNull_WhenNotEnoughMicroRows ()
			{
			// < MinMicroRowsForTraining (40) → модель должна отключаться.
			var rows = BuildMicroRows (count: 20);

			var ml = new MLContext (seed: 42);

			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			Assert.Null (model);
			}

		[Fact]
		public void BuildMicroFlatModel_Throws_OnSingleClassDataset ()
			{
			var rows = BuildMicroRows (count: 60);

			// Превращаем все микро-дни в один класс (все up).
			foreach (var r in rows)
				{
				r.FactMicroUp = true;
				r.FactMicroDown = false;
				}

			var ml = new MLContext (seed: 42);

			Assert.Throws<InvalidOperationException> (() =>
				MicroFlatTrainer.BuildMicroFlatModel (ml, rows));
			}

		[Fact]
		public void BuildMicroFlatModel_TrainsAndAchievesHighAccuracy_OnSyntheticDataset ()
			{
			var rows = BuildMicroRows (count: 120);

			var ml = new MLContext (seed: 42);

			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			Assert.NotNull (model);

			var samples = rows
				.Where (r => r.FactMicroUp || r.FactMicroDown)
				.Select (r => new MlSampleBinary
					{
					Label = r.FactMicroUp,
					Features = MlTrainingUtils.ToFloatFixed (r.Features)
					});

			var data = ml.Data.LoadFromEnumerable (samples);
			var metrics = ml.BinaryClassification.Evaluate (model!.Transform (data));

			Assert.True (metrics.Accuracy > 0.9,
				$"Expected high accuracy on synthetic micro dataset, got {metrics.Accuracy:0.000}");
			}

		private static List<DataRow> BuildMicroRows ( int count )
			{
			var rows = new List<DataRow> (count);
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var date = start.AddDays (i);
				bool up = i % 2 == 0;
				bool down = !up;

				var feats = new double[8];
				// Фича напрямую кодирует направление микро-дня.
				feats[0] = up ? 1.0 : -1.0;

				rows.Add (new DataRow
					{
					Date = date,
					Features = feats,
					FactMicroUp = up,
					FactMicroDown = down
					});
				}

			return rows;
			}
		}
	}
