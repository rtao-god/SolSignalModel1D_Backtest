using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.LowLevel
	{
	public sealed class LeakageRowsBuilderTailTests
		{
		[Fact]
		public void BuildDailyRows_IsFutureBlind_ToTailMutation ()
			{
			const int SyntheticDays = 240;

			BuildSyntheticHistory (
				days: SyntheticDays,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
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

			var resA = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrainA,
				btcWinTrain: btcWinTrainA,
				paxgWinTrain: paxgWinTrainA,
				solAll6h: solAll6hA,
				solAll1m: solAll1mA,
				fngHistory: fngHistory,
				dxySeries: dxyHistory,
				extraDaily: null,
				nyTz: Windowing.NyTz);

			var rowsA = resA.LabeledRows
				.OrderBy (r => r.ToCausalDateUtc ())
				.ToList ();

			Assert.NotEmpty (rowsA);

			var maxCausalDate = rowsA.Last ().ToCausalDateUtc ();
			var trainUntil = maxCausalDate.AddDays (-40);

			var tailStartUtc = trainUntil.AddDays (5);

			MutateFutureTail (
				solWinTrainB,
				btcWinTrainB,
				paxgWinTrainB,
				solAll6hB,
				solAll1mB,
				tailStartUtc: tailStartUtc);

			var resB = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrainB,
				btcWinTrain: btcWinTrainB,
				paxgWinTrain: paxgWinTrainB,
				solAll6h: solAll6hB,
				solAll1m: solAll1mB,
				fngHistory: fngHistory,
				dxySeries: dxyHistory,
				extraDaily: null,
				nyTz: Windowing.NyTz);

			var rowsB = resB.LabeledRows
				.OrderBy (r => r.ToCausalDateUtc ())
				.ToList ();

			var safeRowsA = rowsA
				.Where (r => r.ToCausalDateUtc ().AddDays (8) <= trainUntil)
				.ToList ();

			var safeRowsB = rowsB
				.Where (r => r.ToCausalDateUtc ().AddDays (8) <= trainUntil)
				.ToList ();

			Assert.NotEmpty (safeRowsA);
			Assert.Equal (safeRowsA.Count, safeRowsB.Count);

			for (int i = 0; i < safeRowsA.Count; i++)
				{
				var a = safeRowsA[i];
				var b = safeRowsB[i];

				Assert.Equal (a.ToCausalDateUtc (), b.ToCausalDateUtc ());

				// Labels/facts должны быть стабильны на safe-префиксе.
				Assert.Equal (a.TrueLabel, b.TrueLabel);
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				// Каузальные доменные поля должны быть стабильны на safe-префиксе.
				Assert.Equal (a.Causal.IsMorning, b.Causal.IsMorning);
				Assert.Equal (a.Causal.RegimeDown, b.Causal.RegimeDown);
				Assert.Equal (a.Causal.HardRegime, b.Causal.HardRegime);
				Assert.Equal (a.Causal.MinMove, b.Causal.MinMove);

				// ML-вход: сравниваем ровно то, что реально уходит в обучение (FeaturesVector).
				var fa = a.Causal.FeaturesVector.Span;
				var fb = b.Causal.FeaturesVector.Span;

				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					{
					Assert.Equal (fa[j], fb[j]);
					}
				}
			}

		private static void BuildSyntheticHistory (
			int days,
			out List<Candle6h> solWinTrain,
			out List<Candle6h> btcWinTrain,
			out List<Candle6h> paxgWinTrain,
			out List<Candle6h> solAll6h,
			out List<Candle1m> solAll1m,
			out Dictionary<DateTime, double> fngHistory,
			out Dictionary<DateTime, double> dxyHistory )
			{
			var total6h = days * 4;
			var total1m = total6h * 360;

			var sol6 = new List<Candle6h> (total6h);
			var btc6 = new List<Candle6h> (total6h);
			var paxg6 = new List<Candle6h> (total6h);
			var all1m = new List<Candle1m> (total1m);

			fngHistory = new Dictionary<DateTime, double> (days);
			dxyHistory = new Dictionary<DateTime, double> (days);

			var start = new DateTime (2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var t = start;

			for (int d = 0; d < days; d++)
				{
				var day = t.ToCausalDateUtc ();

				fngHistory[day] = 40.0 + (d % 20);
				dxyHistory[day] = 90.0 + (d % 10);

				for (int k = 0; k < 4; k++)
					{
					double basePrice = 100.0 + d * 0.5 + k;

					sol6.Add (new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice,
						High = basePrice * 1.01,
						Low = basePrice * 0.99,
						Close = basePrice * 1.005
						});

					btc6.Add (new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 10,
						High = basePrice * 10 * 1.01,
						Low = basePrice * 10 * 0.99,
						Close = basePrice * 10 * 1.005
						});

					paxg6.Add (new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 0.2,
						High = basePrice * 0.2 * 1.01,
						Low = basePrice * 0.2 * 0.99,
						Close = basePrice * 0.2 * 1.005
						});

					var minuteStart = t;
					for (int m = 0; m < 360; m++)
						{
						var tm = minuteStart.AddMinutes (m);
						double p = basePrice + Math.Sin ((d * 4 + k) * 0.1 + m * 0.01) * 0.5;

						all1m.Add (new Candle1m
							{
							OpenTimeUtc = tm,
							Open = p,
							High = p * 1.0005,
							Low = p * 0.9995,
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
			static Candle6h Clone6 ( Candle6h c ) => new Candle6h
				{
				OpenTimeUtc = c.OpenTimeUtc,
				Open = c.Open,
				High = c.High,
				Low = c.Low,
				Close = c.Close
				};

			static Candle1m Clone1 ( Candle1m c ) => new Candle1m
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
			static void Mutate6 ( List<Candle6h> xs, DateTime tailStartUtcLocal, double factor )
				{
				for (int i = 0; i < xs.Count; i++)
					{
					var c = xs[i];
					if (c.OpenTimeUtc <= tailStartUtcLocal) continue;

					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;

					xs[i] = c;
					}
				}

			static void Mutate1 ( List<Candle1m> xs, DateTime tailStartUtcLocal, double factor )
				{
				for (int i = 0; i < xs.Count; i++)
					{
					var c = xs[i];
					if (c.OpenTimeUtc <= tailStartUtcLocal) continue;

					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;

					xs[i] = c;
					}
				}

			Mutate6 (solWinTrain, tailStartUtc, 1.5);
			Mutate6 (btcWinTrain, tailStartUtc, 0.7);
			Mutate6 (paxgWinTrain, tailStartUtc, 1.2);
			Mutate6 (solAll6h, tailStartUtc, 1.3);
			Mutate1 (solAll1m, tailStartUtc, 0.8);
			}
		}
	}
