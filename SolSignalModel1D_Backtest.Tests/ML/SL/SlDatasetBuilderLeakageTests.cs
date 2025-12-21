using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.SL
	{
	/// <summary>
	/// Тесты, которые проверяют, что SlDatasetBuilder не допускает утечек:
	/// - SL-сэмплы строятся по 1m-пути,
	/// - но в Train датасет попадают только дни, у которых baseline-exit <= trainUntil.
	/// </summary>
	public sealed class SlDatasetBuilderLeakageTests
		{
		[Fact]
		public void Build_DropsSamplesWhoseBaselineExitGoesBeyondTrainUntil ()
			{
			var nyTz = TimeZones.NewYork;

			// Будний день, NY-утро.
			var entryLocalNy = new DateTime (2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified);
			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocalNy, nyTz);

			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			Assert.True (exitUtc > entryUtc);

			// 1m-окно (короткое, но достаточное для срабатывания TP/SL в первой минуте).
			var sol1m = new List<Candle1m> ();
			double entryPrice = 100.0;
			double tpPct = 0.01;
			double slPct = 0.02;

			for (int i = 0; i < 10; i++)
				{
				var t = entryUtc.AddMinutes (i);

				sol1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					High = entryPrice * (1.0 + tpPct + 0.01),
					Low = entryPrice * (1.0 - slPct - 0.01),
					Close = entryPrice
					});
				}

			// 1h история нужна, потому что SlFeatureBuilder строит фичи из 1h.
			// Важно давать запас до entryUtc, иначе фичи с lookback-окном могут упасть по недостатку данных.
			var sol1h = new List<Candle1h> ();
			var hStart = entryUtc.AddDays (-7);
			var hEnd = exitUtc.AddHours (2);

			for (var t = hStart; t < hEnd; t = t.AddHours (1))
				{
				sol1h.Add (new Candle1h
					{
					OpenTimeUtc = t,
					Open = entryPrice,
					High = entryPrice * 1.001,
					Low = entryPrice * 0.999,
					Close = entryPrice
					});
				}

			// Один утренний omniscient BacktestRecord.
			// Здесь нет "маскировки": это тестовый fixture, который явно задаёт входные значения.
			var rows = new List<BacktestRecord>
				{
				new BacktestRecord
					{
					Causal = new CausalPredictionRecord
					{
						DateUtc = entryUtc,
						MinMove = 0.02,
						PredLabel = 2,
						PredMicroUp = false,
						PredMicroDown = false
					},
					Forward = new ForwardOutcomes
						{
						DateUtc = entryUtc,
						WindowEndUtc = exitUtc,

						TrueLabel = 2,
						FactMicroUp = false,
						FactMicroDown = false,

						Entry = entryPrice,
						MaxHigh24 = entryPrice,
						MinLow24 = entryPrice,
						Close24 = entryPrice,

						// Forward.MinMove может использоваться в отчётах; для теста задаём согласованно.
						MinMove = 0.02,

						// В этом тесте SL-лейблинг использует внешний sol1m, а не Forward.DayMinutes.
						DayMinutes = Array.Empty<Candle1m> ()
						}
					}
				};

			// 6h-свеча для entry (ключ по entryUtc). Важно: это fixture для теста.
			var sol6hDict = new Dictionary<DateTime, Candle6h>
				{
				[entryUtc] = new Candle6h
					{
					OpenTimeUtc = entryUtc,
					Open = entryPrice,
					High = entryPrice,
					Low = entryPrice,
					Close = entryPrice
					}
				};

			// 1) Raw SL-сэмплы напрямую через SlOfflineBuilder (проверяем, что без фильтра они вообще строятся).
			var rawSamples = SlOfflineBuilder.Build (
				rows: rows,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				tpPct: tpPct,
				slPct: slPct,
				strongSelector: null);

			Assert.NotEmpty (rawSamples);
			Assert.All (rawSamples, s => Assert.Equal (entryUtc, s.EntryUtc));

			// 2) trainUntil ставим строго внутри baseline-окна → baseline-exit > trainUntil.
			var trainUntil = entryUtc + TimeSpan.FromTicks ((exitUtc - entryUtc).Ticks / 2);

			var ds = SlDatasetBuilder.Build (
				rows: rows,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntil: trainUntil,
				tpPct: tpPct,
				slPct: slPct,
				strongSelector: null);

			Assert.Empty (ds.Samples);

			// Утренний день не должен остаться в MorningRows,
			// потому что все связанные с ним сэмплы должны быть отрезаны по baseline-exit.
			Assert.DoesNotContain (
				ds.MorningRows,
				r => r.DateUtc.ToCausalDateUtc () == entryUtc.ToCausalDateUtc ());
			}
		}
	}
