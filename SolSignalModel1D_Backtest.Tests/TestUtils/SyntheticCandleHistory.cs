using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
	{
	internal static class SyntheticCandleHistory
		{
		internal static void Build (
			int days,
			out List<Candle6h> solWinTrain,
			out List<Candle6h> btcWinTrain,
			out List<Candle6h> paxgWinTrain,
			out List<Candle6h> solAll6h,
			out List<Candle1h> solAll1h,
			out List<Candle1m> solAll1m,
			out Dictionary<DateTime, Candle6h> sol6hDict,
			out Dictionary<DateTime, double> fngHistory,
			out Dictionary<DateTime, double> dxyHistory )
			{
			var total6h = days * 4;
			var total1h = total6h * 6;
			var total1m = total6h * 360;

			var sol6 = new List<Candle6h> (total6h);
			var btc6 = new List<Candle6h> (total6h);
			var paxg6 = new List<Candle6h> (total6h);

			var all1h = new List<Candle1h> (total1h);
			var all1m = new List<Candle1m> (total1m);

			var dict6 = new Dictionary<DateTime, Candle6h> (total6h);

			fngHistory = new Dictionary<DateTime, double> (days);
			dxyHistory = new Dictionary<DateTime, double> (days);

			var start = new DateTime (2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var t = start;

			for (int d = 0; d < days; d++)
				{
				var day = t.ToCausalDateUtc ();

				// Инвариант: ключи макро-рядов в UTC и в той же "дневной" дискретизации,
				// что и ToCausalDateUtc(), иначе легко поймать ложные "пропуски" по датам.
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

					// Важно: SL-оффлайн билдер берёт entry по ключу EntryUtc.
					// Поэтому словарь должен быть проиндексирован по OpenTimeUtc свечи.
					dict6[cSol.OpenTimeUtc] = cSol;

					// 1h-ряд нужен SL-фичам. Делаем непрерывную почасовую сетку внутри каждого 6h блока.
					for (int h = 0; h < 6; h++)
						{
						var th = t.AddHours (h);

						// Детерминированная "волна", чтобы фичи не были константой.
						double p = basePrice + Math.Sin ((d * 4 + k) * 0.07 + h * 0.11) * 0.6;

						all1h.Add (new Candle1h
							{
							OpenTimeUtc = th,
							Open = p,
							High = p * 1.002,
							Low = p * 0.998,
							Close = p * 1.0005
							});
						}

					// 1m-ряд нужен path-лейблам SL.
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
			solAll1h = all1h;
			solAll1m = all1m;

			sol6hDict = dict6;
			}

		internal static void Clone (
			List<Candle6h> solWinTrainA,
			List<Candle6h> btcWinTrainA,
			List<Candle6h> paxgWinTrainA,
			List<Candle6h> solAll6hA,
			List<Candle1h> solAll1hA,
			List<Candle1m> solAll1mA,
			out List<Candle6h> solWinTrainB,
			out List<Candle6h> btcWinTrainB,
			out List<Candle6h> paxgWinTrainB,
			out List<Candle6h> solAll6hB,
			out List<Candle1h> solAll1hB,
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

			static Candle1h Clone1h ( Candle1h c ) => new Candle1h
				{
				OpenTimeUtc = c.OpenTimeUtc,
				Open = c.Open,
				High = c.High,
				Low = c.Low,
				Close = c.Close
				};

			static Candle1m Clone1m ( Candle1m c ) => new Candle1m
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
			solAll1hB = solAll1hA.Select (Clone1h).ToList ();
			solAll1mB = solAll1mA.Select (Clone1m).ToList ();
			}

		internal static void MutateFutureTail (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			List<Candle1h> solAll1h,
			List<Candle1m> solAll1m,
			DateTime tailStartUtc )
			{
			static void Mutate6 ( List<Candle6h> xs, DateTime t0, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > t0))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			static void Mutate1h ( List<Candle1h> xs, DateTime t0, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > t0))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			static void Mutate1m ( List<Candle1m> xs, DateTime t0, double factor )
				{
				foreach (var c in xs.Where (c => c.OpenTimeUtc > t0))
					{
					c.Open *= factor;
					c.High *= factor;
					c.Low *= factor;
					c.Close *= factor;
					}
				}

			// Инвариант теста: мутация "хвоста" должна затронуть все источники,
			// которые потенциально могут попасть в фичи/лейблы, иначе часть утечек не ловится.
			Mutate6 (solWinTrain, tailStartUtc, 1.4);
			Mutate6 (btcWinTrain, tailStartUtc, 0.75);
			Mutate6 (paxgWinTrain, tailStartUtc, 1.25);

			Mutate6 (solAll6h, tailStartUtc, 1.3);
			Mutate1h (solAll1h, tailStartUtc, 0.9);
			Mutate1m (solAll1m, tailStartUtc, 0.85);
			}
		}
	}
