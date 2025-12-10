using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Tests.Backtest
	{
	/// <summary>
	/// Тест-инвариант: аналитика/бэктест не должны менять базовые поля PredictionRecord.
	/// Если какой-то принтер/движок начнёт мутировать Date/TrueLabel/PredLabel/MinMove и т.п.,
	/// этот тест должен упасть.
	///
	/// Тест специально использует BacktestRunner.Run — тот же путь,
	/// что и консольный пайплайн.
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
		public void BacktestRunner_DoesNotMutate_CorePredictionRecordFields ()
			{
			// Arrange: синтетический набор PredictionRecord с валидными вероятностями.
			// Важно:
			// - TrueLabel == PredLabel, чтобы не ловить p_true=0 в логлоссе/агрегации.
			// - Prob*_Day / Prob*_DayMicro / Prob*_Total суммируются в 1.
			var utcStart = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
			var records = new List<BacktestRecord> ();

			for (int i = 0; i < 20; i++)
				{
				var date = utcStart.AddDays (i);
				int label = i % 3;

				// Одна и та же нормализованная тройка вероятностей по всем слоям.
				double pUp = 0.5;
				double pFlat = 0.2;
				double pDown = 0.3;

				records.Add (new BacktestRecord
					{
					DateUtc = date,
					TrueLabel = label,
					PredLabel = label,

					FactMicroUp = false,
					FactMicroDown = false,

					Entry = 100.0,
					MaxHigh24 = 110.0,
					MinLow24 = 90.0,
					Close24 = 102.0,

					RegimeDown = (i % 5 == 0),
					MinMove = 0.02,

					// Вероятности для дневного слоя
					ProbUp_Day = pUp,
					ProbFlat_Day = pFlat,
					ProbDown_Day = pDown,

					// Вероятности для Day+Micro слоя
					ProbUp_DayMicro = pUp,
					ProbFlat_DayMicro = pFlat,
					ProbDown_DayMicro = pDown,

					// Вероятности для Total слоя (Day+Micro+SL)
					ProbUp_Total = pUp,
					ProbFlat_Total = pFlat,
					ProbDown_Total = pDown,

					// Остальные поля (SL / Delayed / Anti и т.п.) в этом тесте не контролируются
					// и могут использоваться как внутреннее состояние, поэтому оставляем по умолчанию.
					});
				}

			// Утренние точки: используем те же даты, чтобы путь данных был консистентным,
			// но фичи пустые — BacktestRunner/aggregation сейчас их не читает.
			var mornings = records
				.Select (r => new DataRow
					{
					Date = r.DateUtc,
					Features = Array.Empty<double> (),
					Label = r.TrueLabel,
					RegimeDown = r.RegimeDown,
					IsMorning = true
					})
				.ToList ();

			var candles1m = Array.Empty<Candle1m> ();
			var policies = Array.Empty<RollingLoop.PolicySpec> ();

			var config = new BacktestConfig
				{
				DailyStopPct = 0.05,
				DailyTpPct = 0.03
				};

			var trainUntilUtc = utcStart.AddDays (10);

			// Снимаем "снимок" базовых полей до запуска аналитики.
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

			// Act: запускаем тот же рантайм-путь, что и в консоли.
			var runner = new BacktestRunner ();
			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				config: config,
				trainUntilUtc: trainUntilUtc);

			// Assert: проверяем, что базовые поля каждого PredictionRecord не изменились.
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
