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

			if (dateUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("dateUtc must be UTC.", nameof (dateUtc));
			if (trueLabel < 0 || trueLabel > 2)
				throw new ArgumentOutOfRangeException (nameof (trueLabel), trueLabel, "TrueLabel must be in [0..2].");

			var (pUp, pFlat, pDown) = MakeTriProbs (predLabel);

			var causal = new CausalPredictionRecord
				{
				DateUtc = dateUtc,
				FeaturesVector = ReadOnlyMemory<double>.Empty,

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

				// Runtime overlays в этих тестах не участвуют.
				SlProb = null,
				SlHighDecision = null,
				Conf_SlLong = null,
				Conf_SlShort = null,

				DelayedSource = null,
				DelayedEntryAsked = false,
				DelayedEntryUsed = false,
				DelayedWhyNot = null,
				DelayedIntradayTpPct = null,
				DelayedIntradaySlPct = null,
				TargetLevelClass = null
				};

			var forward = new ForwardOutcomes
				{
				DateUtc = dateUtc,
				WindowEndUtc = dateUtc.AddHours (24),

				TrueLabel = trueLabel,
				FactMicroUp = false,
				FactMicroDown = false,

				Entry = 100.0,
				MaxHigh24 = 110.0,
				MinLow24 = 90.0,
				Close24 = 100.0,

				MinMove = 0.01,
				DayMinutes = Array.Empty<Candle1m> ()
				};

			return new BacktestRecord
				{
				Causal = causal,
				Forward = forward
				};
			}

		[Fact]
		public void CheckDailyTrainVsOosAndShuffle_ReturnsSuccess_OnReasonableMetrics ()
			{
			var records = new List<BacktestRecord> ();
			var start = new DateTime (2024, 01, 01, 12, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 250; i++)
				{
				int trueLabel = i % 3;
				int predLabel = (i % 10 < 6) ? trueLabel : (trueLabel + 1) % 3;
				records.Add (MakeRecord (start.AddDays (i), trueLabel, predLabel));
				}

			var trainUntilUtc = start.AddDays (179);
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
			var start = new DateTime (2024, 01, 01, 12, 0, 0, DateTimeKind.Utc);

			const int totalDays = 420;
			const int cut = 200;

			for (int i = 0; i < totalDays; i++)
				{
				int trueLabel = i % 3;

				int predLabel = (i < cut)
					? ((i % 10 < 6) ? trueLabel : (trueLabel + 1) % 3)
					: trueLabel; // OOS-часть "идеальна"

				records.Add (MakeRecord (start.AddDays (i), trueLabel, predLabel));
				}

			var trainUntilUtc = start.AddDays (cut - 1);
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
			var start = new DateTime (2024, 01, 01, 12, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < 80; i++)
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
