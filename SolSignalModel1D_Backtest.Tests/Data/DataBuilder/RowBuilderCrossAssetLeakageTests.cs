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
	/// Тест на cross-asset утечки:
	/// фичи (и TrueLabel) за день D не должны зависеть от будущих BTC/PAXG-данных.
	///
	/// SOL-ряд не мутируется (ни 6h, ни 1m), чтобы:
	/// - TrueLabel оставался стабильным;
	/// - тест ловил именно утечку через cross-asset фичи.
	/// </summary>
	public sealed class RowBuilderCrossAssetLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void Features_DoNotChange_WhenFutureBtcAndPaxgAreMutated ()
			{
			const int total6h = 400;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h = new List<Candle6h> (total6h);
			var btcAll6h_A = new List<Candle6h> (total6h);
			var btcAll6h_B = new List<Candle6h> (total6h);
			var paxgAll6h_A = new List<Candle6h> (total6h);
			var paxgAll6h_B = new List<Candle6h> (total6h);

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);

				double solPrice = 100.0 + i;
				double btcPrice = 50.0 + i * 0.5;
				double goldPrice = 1500.0 + i * 0.2;

				solAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = solPrice,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});

				var btcA = new Candle6h
					{
					OpenTimeUtc = t,
					Open = btcPrice,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					};
				var btcB = new Candle6h
					{
					OpenTimeUtc = t,
					Open = btcPrice,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					};

				var paxgA = new Candle6h
					{
					OpenTimeUtc = t,
					Open = goldPrice,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					};
				var paxgB = new Candle6h
					{
					OpenTimeUtc = t,
					Open = goldPrice,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					};

				btcAll6h_A.Add (btcA);
				btcAll6h_B.Add (btcB);
				paxgAll6h_A.Add (paxgA);
				paxgAll6h_B.Add (paxgB);
				}

			// 1m-ряд по SOL — сплошные минуты, чтобы PathLabeler/MinMove могли работать.
			var solAll1m = new List<Candle1m> (total6h * 6 * 60);
			var minutesStart = start;
			int totalMinutes = total6h * 6 * 60;

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = minutesStart.AddMinutes (i);
				double price = 100.0 + i * 0.0001;

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Open = price,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			// Макро-ряды: ровные, без пропусков.
			var fngBase = new Dictionary<DateTime, double> ();
			var dxyBase = new Dictionary<DateTime, double> ();
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			var firstDate = start.ToCausalDateUtc ().AddDays (-120);
			var lastDate = start.ToCausalDateUtc ().AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50;
				dxyBase[key] = 100.0;
				}

			// A: без мутаций BTC/PAXG.
			var buildA = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h_A,
				paxgWinTrain: paxgAll6h_A,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: NyTz);

			var rowsA = buildA.LabeledRows
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			Assert.True (rowsA.Count > 50, "rowsA слишком мало для теста.");

			// Берём дату из реально построенных строк, чтобы гарантировать, что RowBuilder её не пропустил.
			var entryUtc = rowsA[rowsA.Count / 3].Causal.DateUtc;

			// B: мутируем ТОЛЬКО BTC/PAXG после entryUtc (чистое будущее для дня entryUtc).
			foreach (var c in btcAll6h_B.Where (x => x.OpenTimeUtc > entryUtc))
				{
				c.Open *= 10.0;
				c.Close *= 10.0;
				c.High = c.Close + 100.0;
				c.Low = c.Close - 100.0;
				}

			foreach (var c in paxgAll6h_B.Where (x => x.OpenTimeUtc > entryUtc))
				{
				c.Open *= 5.0;
				c.Close *= 5.0;
				c.High = c.Close + 200.0;
				c.Low = c.Close - 200.0;
				}

			var buildB = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h_B,
				paxgWinTrain: paxgAll6h_B,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: NyTz);

			var rowsB = buildB.LabeledRows
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var rowA = rowsA.SingleOrDefault (r => r.Causal.DateUtc == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.Causal.DateUtc == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// TrueLabel не должен зависеть от будущего BTC/PAXG.
			Assert.Equal (rowA!.TrueLabel, rowB!.TrueLabel);

			// И фичи тоже.
			AssertFeatureVectorsEqual (rowA.Causal, rowB.Causal);
			}

		private static void AssertFeatureVectorsEqual ( CausalDataRow a, CausalDataRow b, int precisionDigits = 10 )
			{
			// Вектор фич должен быть каноническим и фиксированной длины.
			// Сравнение по значениям фиксирует forward-lookups и любые зависимости от будущих рядов.
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
