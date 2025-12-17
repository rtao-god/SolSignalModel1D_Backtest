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
			var records = new List<BacktestRecord> ();

			for (int i = 0; i < 20; i++)
				{
				var dateUtc = utcStart.AddDays (i);
				int trueLabel = i % 3;

				double pUp = 0.5;
				double pFlat = 0.2;
				double pDown = 0.3;

				// Важно: истина и micro-facts живут в ForwardOutcomes,
				// а CausalPredictionRecord содержит только результаты inference + контекст.
				var causal = new CausalPredictionRecord
					{
					DateUtc = dateUtc,
					Features = null, // в этом тесте фичи не используются; проверяем иммутабельность.

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

					RegimeDown = (i % 5 == 0),
					Reason = "test",
					MinMove = 0.02,

					// Runtime-оверлеи в этом тесте не проверяются: оставляем null,
					// чтобы не имитировать «посчитано».
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

					MinMove = 0.02,
					WindowEndUtc = dateUtc.AddDays (1),

					DayMinutes = Array.Empty<Candle1m> ()
					};

				records.Add (new BacktestRecord
					{
					Causal = causal,
					Forward = forward
					});
				}

			// Для этого теста нам не нужен отдельный «morning DTO».
			// Важно только прогнать BacktestRunner и убедиться, что базовые поля записей не мутируются.
			var mornings = records.ToList ();

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
		}
	}
