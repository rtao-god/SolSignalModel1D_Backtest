using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Жёсткий тест на утечки в RowBuilder:
	/// фичи BacktestRecord за день D НЕ должны зависеть от свечей и минуток
	/// после baseline-exit (t_exit).
	/// </summary>
	public sealed class RowBuilderFutureTailLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void Features_DoNotChange_WhenFutureTailAfterExitIsMutated ()
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

			// 1m-ряд: как в уже существующем тесте, просто сплошные минуты
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

			// FNG / DXY / extraDaily как в IndicatorsLeakageTests
			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			var startDay = start.ToCausalDateUtc ();
			var firstDate = startDay.AddDays (-60);
			var lastDate = startDay.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			// A: базовый сценарий
			var rowsA = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h_A,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_A,
				solAll1m: solAll1m_A,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			// выбираем день D не на краях и не в выходной
			int entryIdx = Enumerable.Range (200, 50)
				.First (i =>
				{
					var d = solAll6h_A[i].OpenTimeUtc;
					var ny = TimeZoneInfo.ConvertTimeFromUtc (d, tz);
					return ny.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
				});

			var entryUtc = solAll6h_A[entryIdx].OpenTimeUtc;
			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, tz);

			// B: мутируем ВЕСЬ хвост после exitUtc
			for (int i = 0; i < solAll6h_B.Count; i++)
				{
				if (solAll6h_B[i].OpenTimeUtc >= exitUtc)
					{
					solAll6h_B[i].Close *= 10.0;
					solAll6h_B[i].High = solAll6h_B[i].Close + 5.0;
					solAll6h_B[i].Low = solAll6h_B[i].Close - 5.0;
					}
				}

			for (int i = 0; i < solAll1m_B.Count; i++)
				{
				if (solAll1m_B[i].OpenTimeUtc >= exitUtc)
					{
					var p = solAll1m_B[i].Close * 10.0;
					solAll1m_B[i].Close = p;
					solAll1m_B[i].High = p + 0.01;
					solAll1m_B[i].Low = p - 0.01;
					}
				}

			var rowsB = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h_B,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_B,
				solAll1m: solAll1m_B,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowA = rowsA.SingleOrDefault (r => r.ToCausalDateUtc() == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.ToCausalDateUtc() == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// таргет path-based НЕ должен зависеть от будущего после exit
			Assert.Equal (rowA!.Label, rowB!.Label);

			// а фичи — тоже
			Assert.Equal (rowA.Causal.Features.Length, rowB.Causal.Features.Length);
			for (int i = 0; i < rowA.Causal.Features.Length; i++)
				{
				Assert.Equal (rowA.Causal.Features[i], rowB.Causal.Features[i], 10);
				}
			}
		}
	}
