using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Тест на утечки через extraDaily (funding/OI):
	///
	/// Идея:
	/// - строим два сценария A/B с идентичными SOL/BTC/PAXG/FNG/DXY/1m;
	/// - extraDaily совпадает для всех дней <= entryDate;
	/// - только для дат > entryDate мы сильно мутируем funding/OI в сценарии B;
	/// - проверяем, что для дня entryUtc вектор фич и label не меняются.
	///
	/// Это гарантирует, что RowBuilder при использовании extraDaily не
	/// смотрит в будущее по датам и не использует значения после entryDate.
	/// </summary>
	public sealed class RowBuilderExtraDailyLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void Features_DoNotChange_WhenFutureFundingAndOiAreMutated ()
			{
			var tz = NyTz;

			const int total6h = 400;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

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
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});

				btcAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					});

				paxgAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					});
				}

			// 1m-ряд по SOL: сплошные минуты, как в других leakage-тестах.
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

			// Базовые FNG/DXY — как в других тестах.
			var fngBase = new Dictionary<DateTime, double> ();
			var dxyBase = new Dictionary<DateTime, double> ();

			var firstDate = start.Date.AddDays (-120);
			var lastDate = start.Date.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				// Используем Kind = Utc, чтобы совпасть с openUtc.Date.
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50.0;
				dxyBase[key] = 100.0;
				}

			// extraDaily: создаём базовую серию и её копию.
			// Значения funding/OI зависят от индекса дня, чтобы мутации были заметны.
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

			// A: базовый сценарий — сразу строим строки, чтобы убедиться, что RowBuilder вообще отрабатывает.
			var rowsA_full = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDailyA,
				nyTz: tz);

			Assert.True (rowsA_full.Count > 50, "rowsA_full слишком мало для теста.");

			// Выбираем entryIdx в середине истории, не в выходной.
			int entryIdx = Enumerable.Range (200, 50)
				.First (i =>
				{
					var utc = solAll6h[i].OpenTimeUtc;
					var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, tz);
					return ny.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
				});

			var entryUtc = solAll6h[entryIdx].OpenTimeUtc;
			var entryDate = entryUtc.Date;

			// B: мутируем ТОЛЬКО extraDaily для всех дат > entryDate.
			// Это "чистое будущее" относительно дня entryUtc.
			foreach (var key in extraDailyB.Keys.ToList ())
				{
				if (key.Date > entryDate)
					{
					var ex = extraDailyB[key];
					// Сильная мутация: funding + 0.5, OI * 10.
					extraDailyB[key] = (ex.Funding + 0.5, ex.OI * 10.0);
					}
				}

			// Перестраиваем ряды A/B уже специально для сравнения.
			var rowsA = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDailyA,
				nyTz: tz);

			var rowsB = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDailyB,
				nyTz: tz);

			var rowA = rowsA.SingleOrDefault (r => r.Date == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.Date == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// Label НЕ должен зависеть от будущего funding/OI.
			Assert.Equal (rowA!.Label, rowB!.Label);

			// Фичи тоже не должны зависеть от extraDaily после entryDate.
			Assert.Equal (rowA.Features.Length, rowB.Features.Length);
			for (int i = 0; i < rowA.Features.Length; i++)
				{
				Assert.Equal (rowA.Features[i], rowB.Features[i], 10);
				}
			}
		}
	}
