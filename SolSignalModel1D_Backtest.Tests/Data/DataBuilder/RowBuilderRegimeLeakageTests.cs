using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using Xunit;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Data.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Структурные тесты на утечку через RegimeDown:
	/// 1) RegimeDown для "ранних" дней не должен зависеть от будущего хвоста (t > entryUtc);
	/// 2) RegimeDown для дня D не должен зависеть от того, какой close у свечи,
	///    покрывающей baseline-exit (аналог теста для фич, но для режима).
	///
	/// Важно: эти тесты используют реальный RowBuilder, т.е. реальную логику режима.
	/// </summary>
	public sealed class RowBuilderRegimeLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void RegimeDown_DoesNotChange_WhenAllFutureAfterEntryIsMutated ()
			{
			var tz = NyTz;

			const int total6h = 400;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h_A = new List<Candle6h> ();
			var solAll6h_B = new List<Candle6h> ();
			var btcAll6h = new List<Candle6h> ();
			var paxgAll6h = new List<Candle6h> ();

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);
				double solPrice = 100.0 + i;
				double btcPrice = 50.0 + i * 0.5;
				double goldPrice = 1500.0 + i * 0.2;

				var solA = new Candle6h
					{
					OpenTimeUtc = t,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					};
				var solB = new Candle6h
					{
					OpenTimeUtc = t,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					};
				var btc = new Candle6h
					{
					OpenTimeUtc = t,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					};
				var paxg = new Candle6h
					{
					OpenTimeUtc = t,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					};

				solAll6h_A.Add (solA);
				solAll6h_B.Add (solB);
				btcAll6h.Add (btc);
				paxgAll6h.Add (paxg);
				}

			// Минутки: две копии (A/B), как и для 6h.
			var solAll1m_A = new List<Candle1m> ();
			var solAll1m_B = new List<Candle1m> ();
			var minutesStart = start;
			int totalMinutes = total6h * 6 * 60;

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = minutesStart.AddMinutes (i);
				double price = 100.0 + i * 0.0001;

				var mA = new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					};
				var mB = new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					};

				solAll1m_A.Add (mA);
				solAll1m_B.Add (mB);
				}

			// FNG / DXY: стабильные ряды по датам.
			var fngBase = new Dictionary<DateTime, double> ();
			var dxyBase = new Dictionary<DateTime, double> ();
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			var firstDate = start.Date.AddDays (-60);
			var lastDate = start.Date.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50;
				dxyBase[key] = 100.0;
				}

			// A: базовый сценарий.
			var rowsA = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h_A,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_A,
				solAll1m: solAll1m_A,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: tz);

			Assert.NotEmpty (rowsA);

			// Выбираем entry не слишком близко к краям и не в выходной.
			int entryIdx = Enumerable.Range (200, 50)
				.First (i =>
				{
					var utc = solAll6h_A[i].OpenTimeUtc;
					var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, tz);
					return ny.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
				});

			var entryUtc = solAll6h_A[entryIdx].OpenTimeUtc;

			// B: ломаем всю историю ПОСЛЕ entryUtc (чистое будущее).
			for (int i = 0; i < solAll6h_B.Count; i++)
				{
				if (solAll6h_B[i].OpenTimeUtc > entryUtc)
					{
					solAll6h_B[i].Close *= 10.0;
					solAll6h_B[i].High = solAll6h_B[i].Close + 5.0;
					solAll6h_B[i].Low = solAll6h_B[i].Close - 5.0;
					}
				}

			for (int i = 0; i < solAll1m_B.Count; i++)
				{
				if (solAll1m_B[i].OpenTimeUtc > entryUtc)
					{
					var p = solAll1m_B[i].Close * 10.0;
					solAll1m_B[i].Close = p;
					solAll1m_B[i].High = p + 0.01;
					solAll1m_B[i].Low = p - 0.01;
					}
				}

			var fngB = new Dictionary<DateTime, double> (fngBase);
			var dxyB = new Dictionary<DateTime, double> (dxyBase);

			foreach (var key in fngBase.Keys.ToList ())
				{
				if (key.Date > entryUtc.Date)
					{
					fngB[key] = fngBase[key] + 100;
					dxyB[key] = dxyBase[key] + 50.0;
					}
				}

			var rowsB = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h_B,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_B,
				solAll1m: solAll1m_B,
				fngHistory: fngB,
				dxySeries: dxyB,
				extraDaily: extraDaily,
				nyTz: tz);

			Assert.NotEmpty (rowsB);

			var dictA = rowsA.ToDictionary (r => r.Date, r => r.RegimeDown);
			var dictB = rowsB.ToDictionary (r => r.Date, r => r.RegimeDown);

			// Для всех дат ≤ entryUtc RegimeDown должен совпасть.
			foreach (var kv in dictA)
				{
				var date = kv.Key;
				if (date > entryUtc)
					continue;

				Assert.True (dictB.ContainsKey (date), $"Во втором наборе нет строки для {date:O}");

				Assert.Equal (
					kv.Value,
					dictB[date]);
				}
			}

		/// <summary>
		/// Аналог RowBuilderFeatureLeakageTests, но для RegimeDown:
		/// режим дня D не должен зависеть от того, какой close у свечи,
		/// покрывающей baseline-exit (это чистое будущее).
		/// </summary>
		[Fact]
		public void RegimeDown_DoesNotChange_WhenBaselineExitCloseChanges ()
			{
			var tz = NyTz;

			const int total6h = 300;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h_A = new List<Candle6h> ();
			var solAll6h_B = new List<Candle6h> ();
			var btcAll6h = new List<Candle6h> ();
			var paxgAll6h = new List<Candle6h> ();

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);
				double solPrice = 100.0 + i;
				double btcPrice = 50.0 + i * 0.5;
				double goldPrice = 1500.0 + i * 0.2;

				var solA = new Candle6h
					{
					OpenTimeUtc = t,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					};
				var solB = new Candle6h
					{
					OpenTimeUtc = t,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					};
				var btc = new Candle6h
					{
					OpenTimeUtc = t,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					};
				var paxg = new Candle6h
					{
					OpenTimeUtc = t,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					};

				solAll6h_A.Add (solA);
				solAll6h_B.Add (solB);
				btcAll6h.Add (btc);
				paxgAll6h.Add (paxg);
				}

			// train-окно = вся история.
			var solWinTrain_A = solAll6h_A;
			var solWinTrain_B = solAll6h_B;
			var btcWinTrain = btcAll6h;
			var paxgWinTrain = paxgAll6h;

			// FNG/DXY как в других тестах.
			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();

			var firstDate = start.Date.AddDays (-60);
			var lastDate = start.Date.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			// Минутки только одна копия (они одинаковы в обоих сценариях).
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

			// Выбираем entryIdx не слишком близко к краям и не в выходной.
			int entryIdx = Enumerable.Range (200, 50)
				.First (i =>
				{
					var utc = solWinTrain_A[i].OpenTimeUtc;
					var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, tz);
					var d = ny.DayOfWeek;
					return d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;
				});

			var entryUtc = solWinTrain_A[entryIdx].OpenTimeUtc;

			// Находим baseline-exit и индекс свечи, которая его покрывает, в B-сценарии.
			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, tz);

			int exitIdx = -1;
			for (int i = 0; i < solAll6h_B.Count; i++)
				{
				var startUtc = solAll6h_B[i].OpenTimeUtc;
				var endUtc = (i + 1 < solAll6h_B.Count)
					? solAll6h_B[i + 1].OpenTimeUtc
					: startUtc.AddHours (6);

				if (exitUtc >= startUtc && exitUtc < endUtc)
					{
					exitIdx = i;
					break;
					}
				}

			Assert.True (exitIdx >= 0, "Не удалось найти 6h-свечу, покрывающую baseline exit.");

			// Меняем будущий close только в сценарии B (чистое будущее относительно entry).
			solAll6h_B[exitIdx].Close *= 10.0;
			solAll6h_B[exitIdx].High = solAll6h_B[exitIdx].Close + 1.0;
			solAll6h_B[exitIdx].Low = solAll6h_B[exitIdx].Close - 1.0;

			// Строим строки для обоих сценариев.
			var rowsA = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain_A,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h_A,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowsB = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain_B,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h_B,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowA = rowsA.SingleOrDefault (r => r.Date == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.Date == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// Для дня D режим не должен меняться от изменения future-close.
			Assert.Equal (rowA!.RegimeDown, rowB!.RegimeDown);
			}
		}
	}
