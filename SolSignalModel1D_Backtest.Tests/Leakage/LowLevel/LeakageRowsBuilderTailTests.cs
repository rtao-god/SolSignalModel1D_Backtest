using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.LowLevel
	{
	/// <summary>
	/// Low-level тест на RowBuilder.BuildRowsDaily:
	/// мутируем будущий хвост 6h/1m-свечей и проверяем, что
	/// «безопасный» префикс DataRow (который целиком укладывается в trainUntil
	/// с запасом по горизонту) остаётся неизменным.
	/// </summary>
	public sealed class LeakageRowsBuilderTailTests
		{
		[Fact]
		public void BuildRowsDaily_IsFutureBlind_ToTailMutation ()
			{
			// 1. Синтетическая долгая история.
			BuildSyntheticHistory (
				days: 600,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
				out var fngHistory,
				out var dxyHistory);

			// Клонируем для варианта B.
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

			// 2. Строим базовый набор DataRow (A).
			var rowsA = RowBuilder.BuildRowsDaily (
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

			Assert.NotEmpty (rowsA);

			var maxRowDate = rowsA.Last ().Date;

			// Берём trainUntil немного до конца ряда, чтобы был длинный хвост.
			var trainUntil = maxRowDate.AddDays (-40);

			// 3. Мутируем только хвост после trainUntil для варианта B.

			// Мутируем хвост далеко после trainUntil, чтобы baseline-exit/путь train-строк
			// точно не зацепили замутированную часть.
			var tailStartUtc = trainUntil.AddDays (5);

			MutateFutureTail (
				solWinTrainB,
				btcWinTrainB,
				paxgWinTrainB,
				solAll6hB,
				solAll1mB,
				tailStartUtc: tailStartUtc);

			var rowsB = RowBuilder.BuildRowsDaily (
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

			// 4. Берём только «безопасный» префикс:
			// строки, у которых весь используемый горизонт гарантированно <= trainUntil.
			// Запас 8 дней: baseline-exit и минутный путь короче.
			var safeRowsA = rowsA
				.Where (r => r.Date.AddDays (8) <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			var safeRowsB = rowsB
				.Where (r => r.Date.AddDays (8) <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			Assert.NotEmpty (safeRowsA);
			Assert.Equal (safeRowsA.Count, safeRowsB.Count);

			// 5. Жёстко сравниваем основные поля «безопасного» префикса.
			for (int i = 0; i < safeRowsA.Count; i++)
				{
				var a = safeRowsA[i];
				var b = safeRowsB[i];

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

		// === helpers ===

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
			var sol6 = new List<Candle6h> ();
			var btc6 = new List<Candle6h> ();
			var paxg6 = new List<Candle6h> ();
			var all1m = new List<Candle1m> ();

			fngHistory = new Dictionary<DateTime, double> ();
			dxyHistory = new Dictionary<DateTime, double> ();

			var start = new DateTime (2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var t = start;

			for (int d = 0; d < days; d++)
				{
				var day = t.Date;

				// Простые детерминированные ряды FNG / DXY (double).
				fngHistory[day] = 40.0 + (d % 20);
				dxyHistory[day] = 90.0 + (d % 10);

				for (int k = 0; k < 4; k++)
					{
					double basePrice = 100.0 + d * 0.5 + k;

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
						Open = basePrice * 10,
						High = basePrice * 10 * 1.01,
						Low = basePrice * 10 * 0.99,
						Close = basePrice * 10 * 1.005
						};

					var cPaxg = new Candle6h
						{
						OpenTimeUtc = t,
						Open = basePrice * 0.2,
						High = basePrice * 0.2 * 1.01,
						Low = basePrice * 0.2 * 0.99,
						Close = basePrice * 0.2 * 1.005
						};

					sol6.Add (cSol);
					btc6.Add (cBtc);
					paxg6.Add (cPaxg);

					// 6h → 360 минут 1m.
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

			Mutate6 (solWinTrain, 1.5);
			Mutate6 (btcWinTrain, 0.7);
			Mutate6 (paxgWinTrain, 1.2);
			Mutate6 (solAll6h, 1.3);
			Mutate1 (solAll1m, 0.8);
			}
		}
	}
