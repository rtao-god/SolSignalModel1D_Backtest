using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.Tests.Backtest
	{
	/// <summary>
	/// Инвариант: бэктест/аналитика не должны мутировать базовые поля BacktestRecord
	/// (каузальная часть + forward-исходы).
	/// </summary>
	public sealed class PredictionRecordImmutabilityTests
		{
		private sealed class CoreSnapshot
			{
			public DateTime DateUtc { get; init; }
			public int TrueLabel { get; init; }
			public int PredLabel { get; init; }
			public bool FactMicroUp { get; init; }
			public bool FactMicroDown { get; init; }
			public double Entry { get; init; }
			public double MaxHigh24 { get; init; }
			public double MinLow24 { get; init; }
			public double Close24 { get; init; }
			public bool RegimeDown { get; init; }
			public double MinMove { get; init; }
			}

		[Fact]
		public void BacktestRunner_DoesNotMutate_CoreBacktestRecordFields ()
			{
			var utcStart = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

			var records = new List<BacktestRecord> (20);
			var mornings = new List<LabeledCausalRow> (20);

			for (int i = 0; i < 20; i++)
				{
				var dateUtc = utcStart.AddDays (i);
				int trueLabel = i % 3;

				// Канонический каузальный row для morning-потока:
				// IsMorning валиден (bool), FeaturesVector фиксированной длины и immutable наружу.
				var causalRow = MakeCausalRow (
					dateUtc: dateUtc,
					isMorning: true,
					regimeDown: (i % 5 == 0),
					hardRegime: i % 3,
					minMove: 0.02,
					seed: i);

				// CausalPredictionRecord — это результат inference + runtime-оверлеи.
				// Для теста важно:
				// - FeaturesVector не пустой (иначе тренировки/диагностика могут падать в рантайме);
				// - IsMorning не сеттится (он прокси от Features?.IsMorning).
				double pUp = 0.5, pFlat = 0.2, pDown = 0.3;

				var causal = new CausalPredictionRecord
					{
					DateUtc = dateUtc,
					FeaturesVector = causalRow.FeaturesVector,
					Features = null,

					PredLabel = trueLabel,
					PredLabel_Day = trueLabel,
					PredLabel_DayMicro = trueLabel,
					PredLabel_Total = trueLabel,

					ProbUp_Day = pUp,
					ProbFlat_Day = pFlat,
					ProbDown_Day = pDown,

					ProbUp_DayMicro = pUp,
					ProbFlat_DayMicro = pFlat,
					ProbDown_DayMicro = pDown,

					ProbUp_Total = pUp,
					ProbFlat_Total = pFlat,
					ProbDown_Total = pDown,

					Conf_Day = 0.7,
					Conf_Micro = 0.7,

					MicroPredicted = false,
					PredMicroUp = false,
					PredMicroDown = false,

					RegimeDown = causalRow.RegimeDown,
					Reason = "test",
					MinMove = causalRow.MinMove,

					SlProb = null,
					SlHighDecision = null,
					Conf_SlLong = null,
					Conf_SlShort = null,

					DelayedSource = null,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedIntradayTpPct = null,
					DelayedIntradaySlPct = null,
					TargetLevelClass = null
					};

				var forward = new ForwardOutcomes
					{
					DateUtc = dateUtc,
					TrueLabel = trueLabel,
					FactMicroUp = false,
					FactMicroDown = false,

					Entry = 100.0,
					MaxHigh24 = 110.0,
					MinLow24 = 90.0,
					Close24 = 102.0,

					MinMove = causalRow.MinMove,
					WindowEndUtc = dateUtc.AddDays (1),

					DayMinutes = Array.Empty<Candle1m> ()
					};

				var rec = new BacktestRecord
					{
					Causal = causal,
					Forward = forward
					};

				records.Add (rec);

				// BacktestRunner работает с каузальным morning-потоком как с LabeledCausalRow:
				// это отдельный тип (CausalDataRow), где IsMorning и feature-vector строго каузальны.
				mornings.Add (new LabeledCausalRow (
					causal: causalRow,
					trueLabel: forward.TrueLabel,
					factMicroUp: forward.FactMicroUp,
					factMicroDown: forward.FactMicroDown));
				}

			var candles1m = Array.Empty<Candle1m> ();
			var policies = Array.Empty<RollingLoop.PolicySpec> ();

			var config = new BacktestConfig
				{
				DailyStopPct = 0.05,
				DailyTpPct = 0.03
				};

			var trainUntilUtc = utcStart.AddDays (10);

			var snapshots = records
				.Select (r => new CoreSnapshot
					{
					DateUtc = r.DateUtc,
					TrueLabel = r.TrueLabel,
					PredLabel = r.PredLabel,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,
					Entry = r.Entry,
					MaxHigh24 = r.MaxHigh24,
					MinLow24 = r.MinLow24,
					Close24 = r.Close24,
					RegimeDown = r.RegimeDown,
					MinMove = r.MinMove
					})
				.ToList ();

			var runner = new BacktestRunner ();
			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				config: config,
				trainUntilUtc: trainUntilUtc);

			Assert.Equal (snapshots.Count, records.Count);

			for (int i = 0; i < records.Count; i++)
				{
				var rec = records[i];
				var snap = snapshots[i];

				Assert.Equal (snap.DateUtc, rec.DateUtc);
				Assert.Equal (snap.TrueLabel, rec.TrueLabel);
				Assert.Equal (snap.PredLabel, rec.PredLabel);
				Assert.Equal (snap.FactMicroUp, rec.FactMicroUp);
				Assert.Equal (snap.FactMicroDown, rec.FactMicroDown);
				Assert.Equal (snap.Entry, rec.Entry);
				Assert.Equal (snap.MaxHigh24, rec.MaxHigh24);
				Assert.Equal (snap.MinLow24, rec.MinLow24);
				Assert.Equal (snap.Close24, rec.Close24);
				Assert.Equal (snap.RegimeDown, rec.RegimeDown);
				Assert.Equal (snap.MinMove, rec.MinMove);
				}
			}

		private static CausalDataRow MakeCausalRow (
			DateTime dateUtc,
			bool isMorning,
			bool regimeDown,
			int hardRegime,
			double minMove,
			int seed )
			{
			double s = seed;

			return new CausalDataRow (
				dateUtc: dateUtc,
				regimeDown: regimeDown,
				isMorning: isMorning,
				hardRegime: hardRegime,
				minMove: minMove,

				solRet30: 0.001 * s,
				btcRet30: 0.0005 * s,
				solBtcRet30: 0.001 * s - 0.0005 * s,

				solRet1: 0.0001 * s,
				solRet3: 0.0003 * s,
				btcRet1: 0.00005 * s,
				btcRet3: 0.00015 * s,

				fngNorm: 0.2,
				dxyChg30: 0.0,
				goldChg30: 0.0,

				btcVs200: 0.1,

				solRsiCenteredScaled: 0.0,
				rsiSlope3Scaled: 0.0,

				gapBtcSol1: 0.0,
				gapBtcSol3: 0.0,

				atrPct: 0.02,
				dynVol: 0.015,

				solAboveEma50: 1.0,
				solEma50vs200: 0.1,
				btcEma50vs200: 0.1);
			}
		}
	}
