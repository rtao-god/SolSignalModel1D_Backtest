using System;
using System.Collections.Generic;
using Microsoft.ML;
using Xunit;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Micro
	{
	/// <summary>
	/// Sanity-тесты для микро-модели:
	/// проверяем порог по количеству размеченных микро-дней.
	/// </summary>
	public class MicroLeakageTests
		{
		[Fact]
		public void BuildMicroFlatModel_ReturnsNull_WhenTooFewMicroDays ()
			{
			// Arrange: 10 микро-дней (< 30), фичи минимальные, но валидные.
			var rows = new List<DataRow> ();
			for (int i = 0; i < 10; i++)
				{
				rows.Add (new DataRow
					{
					Date = new DateTime (2025, 1, 1).AddDays (i),
					Features = new[] { 0.1, 0.2, 0.3 },
					FactMicroUp = i % 2 == 0,
					FactMicroDown = i % 2 == 1
					});
				}

			var ml = new MLContext (seed: 42);

			// Act
			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			// Assert: при слишком маленькой выборке модель не должна обучаться.
			Assert.Null (model);
			}

		[Fact]
		public void BuildMicroFlatModel_ReturnsModel_WhenEnoughMicroDays ()
			{
			// Arrange: 40 микро-дней (>= 30), фичи чуть-чуть различаются по дням.
			var rows = new List<DataRow> ();
			for (int i = 0; i < 40; i++)
				{
				var baseDate = new DateTime (2025, 1, 1);

				rows.Add (new DataRow
					{
					Date = baseDate.AddDays (i),

					// Делаем фичи слегка зависящими от i,
					// чтобы не было полностью константного датасета.
					Features = new[]
					{
					0.1 + i * 0.001,
					0.2 + i * 0.001,
					0.3 + i * 0.001
					},

					FactMicroUp = i % 2 == 0,
					FactMicroDown = i % 2 == 1
					});
				}

			var ml = new MLContext (seed: 42);

			// Act
			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			// Assert: при нормальном числе микро-дней модель должна обучиться.
			Assert.NotNull (model);
			}
		}
	}
