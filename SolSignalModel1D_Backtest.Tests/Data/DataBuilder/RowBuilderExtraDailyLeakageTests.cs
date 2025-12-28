using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Тест на утечки через extraDaily (funding/OI):
	/// - строим два сценария A/B с идентичными SOL/BTC/PAXG/FNG/DXY/1m;
	/// - extraDaily совпадает для всех дней <= entryDate;
	/// - только для дат > entryDate мутируем funding/OI в сценарии B;
	/// - проверяем, что для дня entryDate вектор фич и truth не меняются.
	/// </summary>
	public sealed class RowBuilderExtraDailyLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void Features_DoNotChange_WhenFutureFundingAndOiAreMutated ()
			{
			var tz = NyTz;

			const int total6h = 800;
			var start = new DateTime (2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var solAll6h = new List<Candle6h> ();
			var btcAll6h = new List<Candle6h> ();
			var paxgAll6h = new List<Candle6h> ();

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

			var solAll1m = new List<Candle1m> ();
			var minutesStart = start;
			int totalMinutes = total6h * 6 * 60;

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = minutesStart.AddMinutes (i);
				double price = 100.0 + i * 0.0001;

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			var fngBase = new Dictionary<DateTime, double> ();
			var dxyBase = new Dictionary<DateTime, double> ();

			var firstDate = start.ToCausalDateUtc ().AddDays (-120);
			var lastDate = start.ToCausalDateUtc ().AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50.0;
				dxyBase[key] = 100.0;
				}

			var extraDailyA = new Dictionary<DateTime, (double Funding, double OI)> ();
			var extraDailyB = new Dictionary<DateTime, (double Funding, double OI)> ();

			int dayIndex = 0;
			for (var d = firstDate; d <= lastDate; d = d.AddDays (1), dayIndex++)
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

				double funding = 0.0001 * dayIndex;
				double oi = 1_000_000.0 + 10_000.0 * dayIndex;

				var tuple = (funding, oi);
				extraDailyA[key] = tuple;
				extraDailyB[key] = tuple;
				}

			var rowsA_full = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDailyA,
				nyTz: tz).LabeledRows;

			Assert.True (rowsA_full.Count > 50, "rowsA_full слишком мало для теста.");

			int entryIdx = Enumerable.Range (200, 50)
				.First (i =>
				{
					var utc = solAll6h[i].OpenTimeUtc;
					var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, tz);
					return ny.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
						&& CoreNyWindowing.IsNyMorning (new EntryUtc (utc), tz);
				});

			var entryUtc = solAll6h[entryIdx].OpenTimeUtc;
			var entryDate = entryUtc.ToCausalDateUtc ();

			foreach (var key in extraDailyB.Keys.ToList ())
				{
				if (key.ToCausalDateUtc () > entryDate)
					{
					var ex = extraDailyB[key];
					extraDailyB[key] = (ex.Funding + 0.5, ex.OI * 10.0);
					}
				}

			var rowsA = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDailyA,
				nyTz: tz).LabeledRows;

			var rowsB = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDailyB,
				nyTz: tz).LabeledRows;

			var rowA = rowsA.SingleOrDefault (r => r.Causal.EntryUtc.Value.ToCausalDateUtc () == entryDate);
			var rowB = rowsB.SingleOrDefault (r => r.Causal.EntryUtc.Value.ToCausalDateUtc () == entryDate);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// Truth не должен зависеть от future extraDaily.
			Assert.Equal (rowA!.TrueLabel, rowB!.TrueLabel);

			var fa = rowA.Causal.FeaturesVector.Span;
			var fb = rowB.Causal.FeaturesVector.Span;

			Assert.Equal (fa.Length, fb.Length);
			for (int i = 0; i < fa.Length; i++)
				{
				Assert.Equal (fa[i], fb[i], 10);
				}
			}
		}
	}
