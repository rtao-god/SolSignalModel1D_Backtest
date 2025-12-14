using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
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
				// ===== Truth живёт здесь, а не в CausalPredictionRecord =====
				TrueLabel = trueLabel,
				FactMicroUp = false,
				FactMicroDown = false,

				Causal = new CausalPredictionRecord
					{
					DateUtc = dateUtc,

					PredLabel = predLabel,
					PredLabel_Day = predLabel,
					PredLabel_DayMicro = predLabel,
					PredLabel_Total = predLabel,

					ProbUp_Day = pUp,
					ProbFlat_Day = pFlat,
					ProbDown_Day = pDown,

					ProbUp_DayMicro = pUp,
					ProbFlat_DayMicro = pFlat,
					ProbDown_DayMicro = pDown,

					ProbUp_Total = pUp,
					ProbFlat_Total = pFlat,
					ProbDown_Total = pDown,

					Conf_Day = Math.Max (pUp, Math.Max (pFlat, pDown)),
					Conf_Micro = 0.0,

					MicroPredicted = false,
					PredMicroUp = false,
					PredMicroDown = false,

					RegimeDown = false,
					Reason = "test",
					MinMove = 0.01,

					SlProb = 0.0,
					SlHighDecision = false,
					Conf_SlLong = 0.0,
					Conf_SlShort = 0.0,

					DelayedSource = null,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,

					TargetLevelClass = 0
					},

				Forward = new ForwardOutcomes
					{
					DateUtc = dateUtc,
					WindowEndUtc = dateUtc.AddHours (24),

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
			var records = new List<BacktestRecord> ();
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 200; i++)
				{
				int trueLabel = i % 3;
				int predLabel = (i % 10 < 6) ? trueLabel : (trueLabel + 1) % 3;
				records.Add (MakeRecord (start.AddDays (i), trueLabel, predLabel));
				}

			var trainUntilUtc = start.AddDays (149);
			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (records, boundary);

			Assert.NotNull (result);
			Assert.True (result.Success);
			Assert.Empty (result.Errors);

			Assert.False (double.IsNaN (result.Metrics["daily.acc_all"]));
			Assert.False (double.IsNaN (result.Metrics["daily.acc_train"]));
			Assert.False (double.IsNaN (result.Metrics["daily.acc_oos"]));
			}

		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_FlagsLeak_WhenOosAccuracySuspiciouslyHigh ()
			{
			var records = new List<BacktestRecord> ();
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 200; i++)
				{
				int trueLabel = i % 3;

				int predLabel = (i < 100)
					? ((i % 10 < 6) ? trueLabel : (trueLabel + 1) % 3)
					: trueLabel;

				records.Add (MakeRecord (start.AddDays (i), trueLabel, predLabel));
				}

			var trainUntilUtc = start.AddDays (99);
			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (records, boundary);

			Assert.NotNull (result);
			Assert.False (result.Success);

			Assert.Contains (
				result.Errors,
				e => e.Contains ("OOS accuracy", StringComparison.OrdinalIgnoreCase)
				|| e.Contains ("OOS accuracy", StringComparison.Ordinal));
			}

		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_Warns_WhenNoOosPart ()
			{
			var records = new List<BacktestRecord> ();
			var start = new DateTime (2024, 01, 01, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 50; i++)
				{
				int label = i % 3;
				records.Add (MakeRecord (start.AddDays (i), label, label));
				}

			var trainUntilUtc = start.AddDays (1000);
			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var result = DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (records, boundary);

			Assert.NotNull (result);
			Assert.True (result.Success);

			Assert.Contains (
				result.Warnings,
				w => w.Contains ("OOS-часть пуста", StringComparison.OrdinalIgnoreCase)
				|| w.Contains ("OOS-часть пуста", StringComparison.Ordinal));
			}
		}
	}
