using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Тест на "жёсткую" утечку по горизонту:
	/// фичи за день D не должны зависеть ни от каких данных t > entryUtc
	/// (6h, 1m, макро), только от истории до entry.
	///
	/// Label при этом может меняться (мы мутируем forward-path).
	/// </summary>
	public sealed class RowBuilderHorizonLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void Features_DoNotChange_WhenAllFutureAfterEntryIsMutated ()
			{
			var tz = NyTz;

			const int total6h = 400;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h_A = new List<Candle6h> (total6h);
			var solAll6h_B = new List<Candle6h> (total6h);
			var btcAll6h = new List<Candle6h> (total6h);
			var paxgAll6h = new List<Candle6h> (total6h);

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);
				double solPrice = 100.0 + i;
				double btcPrice = 50.0 + i * 0.5;
				double goldPrice = 1500.0 + i * 0.2;

				solAll6h_A.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = solPrice,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});
				solAll6h_B.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = solPrice,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});

				btcAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = btcPrice,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					});

				paxgAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = goldPrice,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					});
				}

			// Минутки: две копии (A/B), как и для 6h.
			var solAll1m_A = new List<Candle1m> (total6h * 6 * 60);
			var solAll1m_B = new List<Candle1m> (total6h * 6 * 60);
			var minutesStart = start;
			int totalMinutes = total6h * 6 * 60;

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = minutesStart.AddMinutes (i);
				double price = 100.0 + i * 0.0001;

				solAll1m_A.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Open = price,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});

				solAll1m_B.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Open = price,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			// Макро-ряды.
			var fngBase = new Dictionary<DateTime, double> ();
			var dxyBase = new Dictionary<DateTime, double> ();
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			var startDay = start.ToCausalDateUtc ();
			var firstDate = startDay.AddDays (-60);
			var lastDate = startDay.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50;
				dxyBase[key] = 100.0;
				}

			// A: базовый сценарий.
			var buildA = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h_A,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_A,
				solAll1m: solAll1m_A,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowsA = buildA.CausalRows
				.OrderBy (r => r.DateUtc)
				.ToList ();

			Assert.True (rowsA.Count > 50, "rowsA слишком мало для теста.");

			var entryUtc = rowsA[rowsA.Count / 3].DateUtc;

			// B: ломаем всё будущее после entryUtc (6h, 1m, макро).
			foreach (var c in solAll6h_B.Where (x => x.OpenTimeUtc > entryUtc))
				{
				c.Open *= 10.0;
				c.Close *= 10.0;
				c.High = c.Close + 5.0;
				c.Low = c.Close - 5.0;
				}

			foreach (var c in solAll1m_B.Where (x => x.OpenTimeUtc > entryUtc))
				{
				c.Open *= 10.0;
				c.Close *= 10.0;
				c.High = c.Close + 0.01;
				c.Low = c.Close - 0.01;
				}

			var fngB = new Dictionary<DateTime, double> (fngBase);
			var dxyB = new Dictionary<DateTime, double> (dxyBase);

			var entryDay = entryUtc.ToCausalDateUtc ();
			foreach (var key in fngB.Keys.ToList ())
				{
                if (key.ToCausalDateUtc() > entryDay)
                {
                    // Контракт FNG: 0..100.
                    fngB[key] = 99.0;

                    dxyB[key] = dxyB[key] + 50.0;
                }
            }

			var buildB = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h_B,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_B,
				solAll1m: solAll1m_B,
				fngHistory: fngB,
				dxySeries: dxyB,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowsB = buildB.CausalRows
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var rowA = rowsA.SingleOrDefault (r => r.DateUtc == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.DateUtc == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// Фичи обязаны быть строго future-blind.
			AssertFeatureVectorsEqual (rowA!, rowB!);
			}

		private static void AssertFeatureVectorsEqual ( CausalDataRow a, CausalDataRow b, int precisionDigits = 10 )
			{
			var va = a.FeaturesVector.Span;
			var vb = b.FeaturesVector.Span;

			Assert.Equal (va.Length, vb.Length);

			for (int i = 0; i < va.Length; i++)
				{
				Assert.Equal (va[i], vb[i], precisionDigits);
				}
			}
		}
	}
