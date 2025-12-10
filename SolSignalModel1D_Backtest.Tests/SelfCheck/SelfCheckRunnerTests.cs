using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using SolSignalModel1D_Backtest.SanityChecks;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Tests.SelfCheck
	{
	/// <summary>
	/// Тесты для SelfCheckRunner на синтетических данных:
	/// - ловим "магическую" точность 100%;
	/// - допускаем разумную точность ~60%.
	/// </summary>
	public class SelfCheckRunnerTests
		{
		[Fact]
		public async Task DailyCheck_FlagsTooGoodTrainAccuracy ()
			{
			// Arrange: 300 дней, все в train, accuracy = 100%.
			var records = new List<BacktestRecord> ();
			var start = new DateTime (2020, 1, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 300; i++)
				{
				var dt = start.AddDays (i);
				int label = i % 3;

				records.Add (new PredictionRecord
					{
					DateUtc = dt,
					TrueLabel = label,
					PredLabel = label
					});
				}

			var ctx = new SelfCheckContext
				{
				Records = records,
				TrainUntilUtc = start.AddYears (10) // всё идёт в train
				};

			// Act
			var result = await SelfCheckRunner.RunAsync (ctx);

			// Assert: ожидаем провал self-check'а.
			Assert.False (result.Success);
			Assert.Contains (result.Errors, e => e.Contains ("train accuracy", StringComparison.OrdinalIgnoreCase));
			}

		[Fact]
		public async Task DailyCheck_AllowsReasonableAccuracy ()
			{
			// Arrange: 300 дней, accuracy ≈ 60% (адекватный уровень).
			var records = new List<BacktestRecord> ();
			var start = new DateTime (2020, 1, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 300; i++)
				{
				var dt = start.AddDays (i);
				int trueLabel = i % 3;
				int predLabel;

				// 6 из 10 случаев предсказываем правильно, 4 — со сдвигом.
				if (i % 10 < 6)
					{
					predLabel = trueLabel;
					}
				else
					{
					predLabel = (trueLabel + 1) % 3;
					}

				records.Add (new PredictionRecord
					{
					DateUtc = dt,
					TrueLabel = trueLabel,
					PredLabel = predLabel
					});
				}

			var ctx = new SelfCheckContext
				{
				Records = records,
				TrainUntilUtc = start.AddDays (200) // часть дней уйдёт в OOS
				};

			// Act
			var result = await SelfCheckRunner.RunAsync (ctx);

			// Assert: жёстких ошибок быть не должно.
			Assert.True (result.Success);
			}
		}
	}
