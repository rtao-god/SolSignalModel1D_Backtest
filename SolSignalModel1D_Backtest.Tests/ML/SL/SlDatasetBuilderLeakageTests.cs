using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.SL;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.SL
	{
	/// <summary>
	/// Тесты, которые проверяют, что SlDatasetBuilder не допускает утечек:
	/// - SL-сэмплы строятся на всей истории (через SlOfflineBuilder),
	///   но потом режутся по baseline-exit <= trainUntil.
	/// </summary>
	public sealed class SlDatasetBuilderLeakageTests
		{
		/// <summary>
		/// Сценарий:
		///
		/// 1) Строим один утренний день:
		///    - есть BacktestRecord с IsMorning = true;
		///    - есть 6h-свеча для entry (Close > 0);
		///    - есть 1m-окно после entry, где уже в первой минуте
		///      одновременно срабатывают и TP, и SL → SlOfflineBuilder
		///      гарантированно создаст хотя бы один сэмпл.
		///
		/// 2) Через SlOfflineBuilder.Build убеждаемся, что raw-сэмплы есть.
		///
		/// 3) Вычисляем baseline-exit этого дня и ставим trainUntil строго
		///    между entry и exit.
		///
		/// 4) Через SlDatasetBuilder.Build строим датасет и ожидаем:
		///    - Samples.Count == 0, т.к. baseline-exit > trainUntil;
		///    - MorningRows пуст или не содержит этот день.
		///
		/// Если кто-то уберёт фильтрацию по baseline-exit в SlDatasetBuilder,
		/// этот тест сломается: сэмпл останется в Samples при trainUntil "внутри" baseline-окна.
		/// </summary>
		[Fact]
		public void Build_DropsSamplesWhoseBaselineExitGoesBeyondTrainUntil ()
			{
			var nyTz = TimeZones.NewYork;

			// Будний день, NY-утро.
			var entryLocalNy = new DateTime (2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified);
			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocalNy, nyTz);

			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			Assert.True (exitUtc > entryUtc);

			// Один утренний BacktestRecord.
			var rows = new List<BacktestRecord>
			{
				new BacktestRecord
				{
					Date = entryUtc,
					IsMorning = true,
					MinMove = 0.02,
					Features = new double[4]
				}
			};

			// 6h-свеча для entry (ключ по Date, как в SlOfflineBuilder).
			var sol6hDict = new Dictionary<DateTime, Candle6h>
				{
				[entryUtc] = new Candle6h
					{
					OpenTimeUtc = entryUtc,
					Close = 100.0,
					High = 100.0,
					Low = 100.0
					}
				};

			// 1m-окно [entry; entry+10m) с "жирными" High/Low, чтобы TP/SL точно сработали.
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
					// Уже в первой минуте High и Low таковы, что одновременно
					// выполняются условия TP и SL; приоритет у SL.
					High = entryPrice * (1.0 + tpPct + 0.01),
					Low = entryPrice * (1.0 - slPct - 0.01),
					Close = entryPrice
					});
				}

			// 1h-свечи для фич нам не обязательны — SlFeatureBuilder нормально работает и с null.
			IReadOnlyList<Candle1h>? sol1h = null;

			// 1) Raw SL-сэмплы напрямую через SlOfflineBuilder.
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

			// Сэмплы с таким entryUtc не должны пройти baseline-фильтр.
			Assert.Empty (ds.Samples);

			// И в MorningRows такой день тоже не должен остаться,
			// т.к. он релевантен только сэмплам, которые отрезаны.
			Assert.DoesNotContain (ds.MorningRows, r => r.Causal.DateUtc == entryUtc);
			}
		}
	}
