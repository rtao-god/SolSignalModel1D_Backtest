using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Тест на cross-asset утечки:
	/// фичи BacktestRecord за день D не должны зависеть от будущих BTC/PAXG-данных.
	///
	/// SOL-ряд при этом НЕ мутируется, чтобы не конфликтовать с уже существующими тестами.
	/// </summary>
	public sealed class RowBuilderCrossAssetLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void Features_DoNotChange_WhenFutureBtcAndPaxgAreMutated ()
			{
			const int total6h = 400;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h = new List<Candle6h> ();
			var btcAll6h_A = new List<Candle6h> ();
			var btcAll6h_B = new List<Candle6h> ();
			var paxgAll6h_A = new List<Candle6h> ();
			var paxgAll6h_B = new List<Candle6h> ();

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);

				double solPrice = 100.0 + i;
				double btcPrice = 50.0 + i * 0.5;
				double goldPrice = 1500.0 + i * 0.2;

				solAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});

				var btcA = new Candle6h
					{
					OpenTimeUtc = t,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					};
				var btcB = new Candle6h
					{
					OpenTimeUtc = t,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					};

				var paxgA = new Candle6h
					{
					OpenTimeUtc = t,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					};
				var paxgB = new Candle6h
					{
					OpenTimeUtc = t,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					};

				btcAll6h_A.Add (btcA);
				btcAll6h_B.Add (btcB);
				paxgAll6h_A.Add (paxgA);
				paxgAll6h_B.Add (paxgB);
				}

			// 1m-ряд по SOL — как в других leakage-тестах.
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

			// Базовые FNG/DXY
			var fngBase = new Dictionary<DateTime, double> ();
			var dxyBase = new Dictionary<DateTime, double> ();
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			var firstDate = start.ToCausalDateUtc().AddDays (-120);
			var lastDate = start.ToCausalDateUtc().AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50;
				dxyBase[key] = 100.0;
				}

			// A: базовый сценарий без мутаций BTC/PAXG.
			var rowsA = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h_A,
				paxgWinTrain: paxgAll6h_A,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: NyTz);

			Assert.True (rowsA.Count > 50, "rowsA слишком мало для теста");

			// Выбор entry-дня: середина истории, не выходной.
			int entryIdx = Enumerable.Range (200, 50)
				.First (i =>
				{
					var utc = solAll6h[i].OpenTimeUtc;
					var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, NyTz);
					return ny.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
				});

			var entryUtc = solAll6h[entryIdx].OpenTimeUtc;

			// B: мутируем ТОЛЬКО BTC/PAXG после entryUtc (чистое будущее для дня entryUtc).
			for (int i = 0; i < btcAll6h_B.Count; i++)
				{
				if (btcAll6h_B[i].OpenTimeUtc > entryUtc)
					{
					btcAll6h_B[i].Close *= 10.0;
					btcAll6h_B[i].High = btcAll6h_B[i].Close + 100.0;
					btcAll6h_B[i].Low = btcAll6h_B[i].Close - 100.0;
					}
				}

			for (int i = 0; i < paxgAll6h_B.Count; i++)
				{
				if (paxgAll6h_B[i].OpenTimeUtc > entryUtc)
					{
					paxgAll6h_B[i].Close *= 5.0;
					paxgAll6h_B[i].High = paxgAll6h_B[i].Close + 200.0;
					paxgAll6h_B[i].Low = paxgAll6h_B[i].Close - 200.0;
					}
				}

			var rowsB = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h_B,
				paxgWinTrain: paxgAll6h_B,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: NyTz);

			var rowA = rowsA.SingleOrDefault (r => r.ToCausalDateUtc() == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.ToCausalDateUtc() == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// Label не должен зависеть от будущего BTC/PAXG.
			Assert.Equal (rowA!.Label, rowB!.Label);

			// Фичи — тоже.
			Assert.Equal (rowA.Causal.Features.Length, rowB.Causal.Features.Length);
			for (int i = 0; i < rowA.Causal.Features.Length; i++)
				{
				Assert.Equal (rowA.Causal.Features[i], rowB.Causal.Features[i], 10);
				}
			}
		}
	}
