using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;
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
		private static BacktestRecord MakeRecord ( DateTime dateUtc, int trueLabel, int predLabel )
			{
			static (double Up, double Flat, double Down) MakeTriProbs ( int cls )
				{
				const double Hi = 0.90;
				const double Lo = 0.05;

				return cls switch
					{
						2 => (Hi, Lo, Lo),
						1 => (Lo, Hi, Lo),
						0 => (Lo, Lo, Hi),
						_ => throw new ArgumentOutOfRangeException (nameof (cls), cls, "PredLabel must be in [0..2].")
						};
				}

			var (pUp, pFlat, pDown) = MakeTriProbs (predLabel);

			return new BacktestRecord
				{
				Causal = new CausalPredictionRecord
					{
					DateUtc = dateUtc,
					TrueLabel = trueLabel,
					PredLabel = predLabel,
					PredLabel_Day = predLabel,
					PredLabel_DayMicro = predLabel,

					ProbUp_Day = pUp,
					ProbFlat_Day = pFlat,
					ProbDown_Day = pDown,

					ProbUp_DayMicro = pUp,
					ProbFlat_DayMicro = pFlat,
					ProbDown_DayMicro = pDown,

					ProbUp_Total = pUp,
					ProbFlat_Total = pFlat,
					ProbDown_Total = pDown,

					Conf_Day = Math.Max (pUp, Math.Max (pFlat, pDown))
					},

				Forward = new ForwardOutcomes
					{
					DateUtc = dateUtc,
					WindowEndUtc = dateUtc.AddHours (24),

					// Эти поля в данном наборе тестов не участвуют в проверках,
					// но forward-часть должна быть валидной структурно.
					Entry = 100.0,
					MaxHigh24 = 110.0,
					MinLow24 = 90.0,
					Close24 = 100.0,
					MinMove = 0.01,
					DayMinutes = Array.Empty<Candle1m> ()
					}
				};
			}

		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_ReturnsSuccess_OnReasonableMetrics ()
			{
			// Здесь строится выборка с умеренной точностью (~60%)
			// как на train, так и на OOS. Ошибок быть не должно.

			var records = new List<BacktestRecord> ();

			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 200; i++)
				{
				int trueLabel = i % 3;

				// Простейшая схема: 60% попаданий, 40% мимо.
				// Через i % 10 < 6 задаётся примерно 6 из 10 совпадений.
				int predLabel = (i % 10 < 6)
					? trueLabel
					: (trueLabel + 1) % 3;

				records.Add (MakeRecord (start.AddDays (i), trueLabel, predLabel));
				}

			// Первые 150 дней считаем train, остальные 50 — OOS.
			var trainUntilUtc = start.AddDays (149);

			// Важно: boundary требует NY TZ, потому что сегментация основана на baseline-exit.
			// Для теста достаточно использовать тот же TZ, что используется в контракте Windowing.
			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
				records,
				boundary);

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

			var records = new List<BacktestRecord> ();
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

				records.Add (MakeRecord (start.AddDays (i), trueLabel, predLabel));
				}

			var trainUntilUtc = start.AddDays (99);
			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
				records,
				boundary);

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

			var records = new List<BacktestRecord> ();
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 50; i++)
				{
				int label = i % 3;
				records.Add (MakeRecord (start.AddDays (i), label, label));
				}

			// trainUntil ставим после последнего дня — OOS не будет.
			var trainUntilUtc = start.AddDays (1000);
			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
				records,
				boundary);

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
