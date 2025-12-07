using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Тесты для DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle:
	/// - сценарий без проблем;
	/// - сценарий с подозрительно высокой точностью на OOS;
	/// - сценарий без OOS-части.
	/// </summary>
	public sealed class DailyLeakageChecksTests
		{
		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_ReturnsSuccess_OnReasonableMetrics ()
			{
			// Здесь строится выборка с умеренной точностью (~60%)
			// как на train, так и на OOS. Ошибок быть не должно.

			var records = new List<PredictionRecord> ();

			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 200; i++)
				{
				int trueLabel = i % 3;

				// Простейшая схема: 60% попаданий, 40% мимо.
				// Через i % 10 < 6 задаётся примерно 6 из 10 совпадений.
				int predLabel = (i % 10 < 6)
					? trueLabel
					: (trueLabel + 1) % 3;

				records.Add (new PredictionRecord
					{
					DateUtc = start.AddDays (i),
					TrueLabel = trueLabel,
					PredLabel = predLabel
					});
				}

			// Первые 150 дней считаем train, остальные 50 — OOS.
			var trainUntilUtc = start.AddDays (149);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
				records,
				trainUntilUtc);

			Assert.NotNull (result);
			Assert.True (result.Success);

			// В этом сценарии не должно быть жёстких ошибок.
			Assert.Empty (result.Errors);

			// Дополнительно проверяем, что метрики вообще есть.
			Assert.False (double.IsNaN (result.Metrics["daily.acc_all"]));
			Assert.False (double.IsNaN (result.Metrics["daily.acc_train"]));
			Assert.False (double.IsNaN (result.Metrics["daily.acc_oos"]));
			}

		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_FlagsLeak_WhenOosAccuracySuspiciouslyHigh ()
			{
			// Сценарий: train нормальный, OOS почти идеальный.
			// Должна появиться ошибка про подозрительно высокую точность на OOS.

			var records = new List<PredictionRecord> ();
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 200; i++)
				{
				int trueLabel = i % 3;

				int predLabel;
				if (i < 100)
					{
					// Train: те же 60% попаданий, как в предыдущем тесте.
					predLabel = (i % 10 < 6)
						? trueLabel
						: (trueLabel + 1) % 3;
					}
				else
					{
					// OOS: почти идеальная модель (100% попаданий).
					predLabel = trueLabel;
					}

				records.Add (new PredictionRecord
					{
					DateUtc = start.AddDays (i),
					TrueLabel = trueLabel,
					PredLabel = predLabel
					});
				}

			var trainUntilUtc = start.AddDays (99);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
				records,
				trainUntilUtc);

			Assert.NotNull (result);

			// В этом сценарии Success должен быть false из-за ошибки по OOS.
			Assert.False (result.Success);

			Assert.Contains (
				result.Errors,
				e => e.Contains ("OOS accuracy", StringComparison.OrdinalIgnoreCase)
				|| e.Contains ("OOS accuracy", StringComparison.Ordinal));
			}

		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_Warns_WhenNoOosPart ()
			{
			// Сценарий: все дни попадают в train-часть, OOS нет.
			// Должно быть предупреждение про пустую OOS-часть.

			var records = new List<PredictionRecord> ();
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 50; i++)
				{
				int label = i % 3;

				records.Add (new PredictionRecord
					{
					DateUtc = start.AddDays (i),
					TrueLabel = label,
					PredLabel = label
					});
				}

			// trainUntil ставим после последнего дня — OOS не будет.
			var trainUntilUtc = start.AddDays (1000);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
				records,
				trainUntilUtc);

			Assert.NotNull (result);
			Assert.True (result.Success);

			// Должен быть warning про пустую OOS-часть.
			Assert.Contains (
				result.Warnings,
				w => w.Contains ("OOS-часть пуста", StringComparison.OrdinalIgnoreCase)
				|| w.Contains ("OOS-часть пуста", StringComparison.Ordinal));
			}
		}
	}
