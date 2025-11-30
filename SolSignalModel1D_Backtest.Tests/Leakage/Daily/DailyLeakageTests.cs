using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;
using Xunit;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Базовый smoke-тест для DailyLeakageChecks:
	/// - конструируется маленький train+OOS датасет;
	/// - проверяется, что RunFirstBlock возвращает метрики и не падает.
	/// </summary>
	public sealed class DailyLeakageTests
		{
		[Fact]
		public void RunFirstBlock_ReturnsMetricsAndSuccess_OnReasonableData ()
			{
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			var rows = new List<DataRow> ();
			var records = new List<PredictionRecord> ();

			// 4 train-дня с идеальным попаданием.
			for (int i = 0; i < 4; i++)
				{
				var dt = start.AddDays (i);

				rows.Add (new DataRow
					{
					Date = dt,
					IsMorning = true,
					Label = i % 3
					});

				records.Add (new PredictionRecord
					{
					DateUtc = dt,
					TrueLabel = i % 3,
					PredLabel = i % 3
					});
				}

			// 4 OOS-дня с чуть худшими предсказаниями.
			for (int i = 4; i < 8; i++)
				{
				var dt = start.AddDays (i);

				rows.Add (new DataRow
					{
					Date = dt,
					IsMorning = true,
					Label = i % 3
					});

				records.Add (new PredictionRecord
					{
					DateUtc = dt,
					TrueLabel = i % 3,
					PredLabel = (i + 1) % 3
					});
				}

			// Первые 4 дня считаем train.
			var trainUntilUtc = start.AddDays (3);

			var result = DailyLeakageChecks.RunFirstBlock (rows, records, trainUntilUtc);

			Assert.NotNull (result);
			Assert.Equal ("daily", result.CheckName);

			// Базовые метрики должны присутствовать.
			Assert.True (result.Metrics.ContainsKey ("train.count"));
			Assert.True (result.Metrics.ContainsKey ("oos.count"));
			Assert.True (result.Metrics.ContainsKey ("train.acc"));
			Assert.True (result.Metrics.ContainsKey ("oos.acc"));
			}
		}
	}
