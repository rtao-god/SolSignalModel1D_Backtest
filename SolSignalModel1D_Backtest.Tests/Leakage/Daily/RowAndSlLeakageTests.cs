using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Набор leakage-тестов по слоям:
	/// - RowBuilder → DataRow;
	/// - DailyDatasetBuilder;
	/// - SlDatasetBuilder.
	///
	/// Идея везде одна: мутируем хвост свечей далеко после asOf/trainUntil
	/// и проверяем, что для дат <= asOf/trainUntil результаты не меняются.
	/// </summary>
	public sealed class RowAndSlLeakageTests
		{
		// Уменьшаем синтетический диапазон без потери смысла тестов.
		private const int SyntheticDays = 240;

		// === 1. RowBuilder: future-blind по хвосту ===

		[Fact]
		public void RowBuilder_DailyRows_AreFutureBlind_ToTailMutation ()
			{
			// Синтетическая история 6h/1m.
			BuildSyntheticHistory (
				days: SyntheticDays,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
				out _,
				out var fngHistory,
				out var dxyHistory);

			// Клон для мутированной ветки.
			CloneHistory (
				solWinTrainA,
				btcWinTrainA,
				paxgWinTrainA,
				solAll6hA,
				solAll1mA,
				out var solWinTrainB,
				out var btcWinTrainB,
				out var paxgWinTrainB,
				out var solAll6hB,
				out var solAll1mB);

			// Строим полный набор строк по исходным данным.
			var rowsAAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainA,
					btcWinTrain: btcWinTrainA,
					paxgWinTrain: paxgWinTrainA,
					solAll6h: solAll6hA,
					solAll1m: solAll1mA,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			Assert.NotEmpty (rowsAAll);

			var minDate = rowsAAll.First ().Date;
			var maxDate = rowsAAll.Last ().Date;

			// Берём asOf как середину диапазона дат RowBuilder'а.
			var cutoffTicks = minDate.Ticks + (long) ((maxDate.Ticks - minDate.Ticks) * 0.5);
			var asOfUtc = new DateTime (cutoffTicks, DateTimeKind.Utc);

			// Мутируем только хвост после asOf + 5 дней,
			// чтобы никакой baseline/path-доступ до asOf не видел замутированных свечей.
			var tailStartUtc = asOfUtc.AddDays (5);

			MutateFutureTail (
				solWinTrainB,
				btcWinTrainB,
				paxgWinTrainB,
				solAll6hB,
				solAll1mB,
				tailStartUtc: tailStartUtc);

			var rowsBAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainB,
					btcWinTrain: btcWinTrainB,
					paxgWinTrain: paxgWinTrainB,
					solAll6h: solAll6hB,
					solAll1m: solAll1mB,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			// Сравниваем только префикс до asOf.
			var aPrefix = rowsAAll
				.Where (r => r.Date <= asOfUtc)
				.ToList ();

			var bPrefix = rowsBAll
				.Where (r => r.Date <= asOfUtc)
				.ToList ();

			AssertRowsEqual (aPrefix, bPrefix);
			}

		// === 2. DailyDatasetBuilder: future-blind по trainUntil ===

		[Fact]
		public void DailyDatasetBuilder_TrainRows_AreFutureBlind_ToTailMutation ()
			{
			BuildSyntheticHistory (
				days: SyntheticDays,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
				out _,
				out var fngHistory,
				out var dxyHistory);

			CloneHistory (
				solWinTrainA,
				btcWinTrainA,
				paxgWinTrainA,
				solAll6hA,
				solAll1mA,
				out var solWinTrainB,
				out var btcWinTrainB,
				out var paxgWinTrainB,
				out var solAll6hB,
				out var solAll1mB);

			var rowsAAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainA,
					btcWinTrain: btcWinTrainA,
					paxgWinTrain: paxgWinTrainA,
					solAll6h: solAll6hA,
					solAll1m: solAll1mA,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			Assert.NotEmpty (rowsAAll);

			var minDate = rowsAAll.First ().Date;
			var maxDate = rowsAAll.Last ().Date;

			var cutoffTicks = minDate.Ticks + (long) ((maxDate.Ticks - minDate.Ticks) * 0.6);
			var trainUntil = new DateTime (cutoffTicks, DateTimeKind.Utc);

			var tailStartUtc = trainUntil.AddDays (5);

			MutateFutureTail (
				solWinTrainB,
				btcWinTrainB,
				paxgWinTrainB,
				solAll6hB,
				solAll1mB,
				tailStartUtc: tailStartUtc);

			var rowsBAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainB,
					btcWinTrain: btcWinTrainB,
					paxgWinTrain: paxgWinTrainB,
					solAll6h: solAll6hB,
					solAll1m: solAll1mB,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			var allRowsA = rowsAAll
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			var allRowsB = rowsBAll
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			Assert.NotEmpty (allRowsA);
			Assert.Equal (allRowsA.Count, allRowsB.Count);

			var dsA = DailyDatasetBuilder.Build (
				allRows: allRowsA,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			var dsB = DailyDatasetBuilder.Build (
				allRows: allRowsB,
				trainUntil: trainUntil,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MoveTrainRows, dsB.MoveTrainRows);
			AssertRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertRowsEqual (dsA.DirDownRows, dsB.DirDownRows);
			}

		// === 3. SlDatasetBuilder: future-blind по trainUntil ===

		[Fact]
		public void SlDatasetBuilder_Samples_AreFutureBlind_ToTailMutation ()
			{
			BuildSyntheticHistory (
				days: SyntheticDays,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
				out var sol6hDictA,
				out var fngHistory,
				out var dxyHistory);

			CloneHistory (
				solWinTrainA,
				btcWinTrainA,
				paxgWinTrainA,
				solAll6hA,
				solAll1mA,
				out var solWinTrainB,
				out var btcWinTrainB,
				out var paxgWinTrainB,
				out var solAll6hB,
				out var solAll1mB);

			var rowsAAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainA,
					btcWinTrain: btcWinTrainA,
					paxgWinTrain: paxgWinTrainA,
					solAll6h: solAll6hA,
					solAll1m: solAll1mA,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			Assert.NotEmpty (rowsAAll);

			var minDate = rowsAAll.First ().Date;
			var maxDate = rowsAAll.Last ().Date;

			var cutoffTicks = minDate.Ticks + (long) ((maxDate.Ticks - minDate.Ticks) * 0.6);
			var trainUntil = new DateTime (cutoffTicks, DateTimeKind.Utc);

			var tailStartUtc = trainUntil.AddDays (5);

			MutateFutureTail (
				solWinTrainB,
				btcWinTrainB,
				paxgWinTrainB,
				solAll6hB,
				solAll1mB,
				tailStartUtc: tailStartUtc);

			var rowsBAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainB,
					btcWinTrain: btcWinTrainB,
					paxgWinTrain: paxgWinTrainB,
					solAll6h: solAll6hB,
					solAll1m: solAll1mB,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Date)
				.ToList ();

			var allRowsA = rowsAAll
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			var allRowsB = rowsBAll
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			Assert.NotEmpty (allRowsA);
			Assert.Equal (allRowsA.Count, allRowsB.Count);

			const double TpPct = 0.01;
			const double SlPct = 0.02;

			var slDsA = SlDatasetBuilder.Build (
				rows: allRowsA,
				sol1h: null,
				sol1m: solAll1mA,
				sol6hDict: sol6hDictA,
				trainUntil: trainUntil,
				tpPct: TpPct,
				slPct: SlPct,
				strongSelector: null);

			var slDsB = SlDatasetBuilder.Build (
				rows: allRowsB,
				sol1h: null,
				sol1m: solAll1mB,
				sol6hDict: sol6hDictA,
				trainUntil: trainUntil,
				tpPct: TpPct,
				slPct: SlPct,
				strongSelector: null);

			// Чтобы тест был осмысленным, датасет должен быть непустым.
			Assert.True (slDsA.Samples.Count > 0, "SL leakage test: synthetic SL dataset is empty.");
			Assert.Equal (slDsA.Samples.Count, slDsB.Samples.Count);

			// Утро: MorningRows должны совпадать A/B.
			AssertRowsEqual (slDsA.MorningRows, slDsB.MorningRows);

			// Сэмплы SL: проверяем label + фичи.
			var sa = slDsA.Samples;
			var sb = slDsB.Samples;

			for (int i = 0; i < sa.Count; i++)
				{
				var a = sa[i];
				var b = sb[i];

				Assert.Equal (a.Label, b.Label);

				var fa = a.Features ?? Array.Empty<float> ();
				var fb = b.Features ?? Array.Empty<float> ();

				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}

		// === вспомогательные методы (скопированы из E2E) ===

		private static void AssertRowsEqual ( List<DataRow> xs, List<DataRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.Date, b.Date);
				Assert.Equal (a.Label, b.Label);
				Assert.Equal (a.IsMorning, b.IsMorning);
				Assert.Equal (a.MinMove, b.MinMove);
				Assert.Equal (a.RegimeDown, b.RegimeDown);

				var fa = a.Features ?? Array.Empty<double> ();
				var fb = b.Features ?? Array.Empty<double> ();

				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}

		private static void BuildSyntheticHistory (
			int days,
			out List<Candle6h> solWinTrain,
			out List<Candle6h> btcWinTrain,
			out List<Candle6h> paxgWinTrain,
			out List<Candle6h> solAll6h,
			out List<Candle1m> solAll1m,
			out Dictionary<DateTime, Candle6h> sol6hDict,
			out Dictionary<DateTime, double> fngHistory,
			out Dictionary<DateTime, double> dxyHistory )
			{
			var total6h = days * 4;
			var total1m = total6h * 360;

			var sol6 = new List<Candle6h> (total6h);
			var btc6 = new List<Candle6h> (total6h);
			var paxg6 = new List<Candle6h> (total6h);
			var all1m = new List<Candle1m> (total1m);
			var dict6 = new Dictionary<DateTime, Candle6h> (total6h);

			fngHistory = new Dictionary<DateTime, double> (days);
			dxyHistory = new Dictionary<DateTime, double> (days);

			var start = new DateTime (2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var t = start;

			for (int d = 0; d < days; d++)
				{
				var day = t.Date;

				fngHistory[day] = 50.0 + (d % 10);
				dxyHistory[day] = 95.0 + (d % 5);

				for (int k = 0; k < 4; k++)
					{
					double basePrice = 120.0 + d * 0.4 + k;

					var cSol = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice,
						High = basePrice * 1.01,
						Low = basePrice * 0.99,
						Close = basePrice * 1.005
						};

					var cBtc = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 8,
						High = basePrice * 8 * 1.01,
						Low = basePrice * 8 * 0.99,
						Close = basePrice * 8 * 1.005
						};

					var cPaxg = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 0.25,
						High = basePrice * 0.25 * 1.01,
						Low = basePrice * 0.25 * 0.99,
						Close = basePrice * 0.25 * 1.005
						};

					sol6.Add (cSol);
					btc6.Add (cBtc);
					paxg6.Add (cPaxg);
					dict6[cSol.OpenTimeUtc] = cSol;

					var minuteStart = t;
					for (int m = 0; m < 360; m++)
						{
						var tm = minuteStart.AddMinutes (m);
						double p = basePrice + Math.Sin ((d * 4 + k) * 0.07 + m * 0.01) * 0.4;

						all1m.Add (new Candle1m
							{
							OpenTimeUtc = tm,
							Open = p,
							High = p * 1.0006,
							Low = p * 0.9994,
							Close = p
							});
						}

					t = t.AddHours (6);
					}
				}

			solWinTrain = sol6;
			btcWinTrain = btc6;
			paxgWinTrain = paxg6;
			solAll6h = sol6;
			solAll1m = all1m;
			sol6hDict = dict6;
			}

		private static void CloneHistory (
			List<Candle6h> solWinTrainA,
			List<Candle6h> btcWinTrainA,
			List<Candle6h> paxgWinTrainA,
			List<Candle6h> solAll6hA,
			List<Candle1m> solAll1mA,
			out List<Candle6h> solWinTrainB,
			out List<Candle6h> btcWinTrainB,
			out List<Candle6h> paxgWinTrainB,
			out List<Candle6h> solAll6hB,
			out List<Candle1m> solAll1mB )
			{
			Candle6h Clone6 ( Candle6h c ) => new Candle6h
				{
				OpenTimeUtc = c.OpenTimeUtc,
				Open = c.Open,
				High = c.High,
				Low = c.Low,
				Close = c.Close
				};

			Candle1m Clone1 ( Candle1m c ) => new Candle1m
				{
				OpenTimeUtc = c.OpenTimeUtc,
				Open = c.Open,
				High = c.High,
				Low = c.Low,
				Close = c.Close
				};

			solWinTrainB = solWinTrainA.Select (Clone6).ToList ();
			btcWinTrainB = btcWinTrainA.Select (Clone6).ToList ();
			paxgWinTrainB = paxgWinTrainA.Select (Clone6).ToList ();
			solAll6hB = solAll6hA.Select (Clone6).ToList ();
			solAll1mB = solAll1mA.Select (Clone1).ToList ();
			}

		private static void MutateFutureTail (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			List<Candle1m> solAll1m,
			DateTime tailStartUtc )
			{
			void Mutate6 ( List<Candle6h> xs, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > tailStartUtc))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			void Mutate1 ( List<Candle1m> xs, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > tailStartUtc))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			Mutate6 (solWinTrain, 1.4);
			Mutate6 (btcWinTrain, 0.75);
			Mutate6 (paxgWinTrain, 1.25);
			Mutate6 (solAll6h, 1.3);
			Mutate1 (solAll1m, 0.85);
			}
		}
	}
