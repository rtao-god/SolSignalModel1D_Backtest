using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using Xunit;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Структурный тест: фичи BacktestRecord для дня D не должны зависеть
	/// от того, какой close у свечи, покрывающей baseline-exit.
	///
	/// Если в RowBuilder в фичи подмешан SolFwd1 (как сейчас через EnableLeakageHackForTests),
	/// этот тест будет падать.
	/// </summary>
	public sealed class RowBuilderFeatureLeakageTests
		{
		[Fact]
		public void Features_DoNotChange_WhenBaselineExitCloseChanges ()
			{
			var tz = CoreWindowing.NyTz;

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

			// Для простоты считаем train-окном всю историю.
			var solWinTrain_A = solAll6h_A;
			var solWinTrain_B = solAll6h_B;
			var btcWinTrain = btcAll6h;
			var paxgWinTrain = paxgAll6h;

			// FNG и DXY: стабильные ряды по датам.
			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();

			var startDay = start.ToCausalDateUtc ();
			var firstDate = startDay.AddDays (-60);
			var lastDate = startDay.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				// Важно: Kind = Utc, чтобы совпадать с openUtc.ToCausalDateUtc().
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			// Extra daily пока не используем.
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			// Простая 1m-серия: сплошные минуты по времени.
			// Структура Candle1m предполагается совместимой с использованием в PathLabeler.
			var solAll1m = new List<Candle1m> ();
			var minutesStart = start;
			int totalMinutes = total6h * 6 * 60; // достаточно покрыть все baseline-окна.

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

			// Выбираем день не слишком близко к началу и не к концу,
			// чтобы были и ретурны 30d, и EMA200, и достаточно хвоста до maxExit.
			int entryIdx = Enumerable.Range (200, 50)
			.First (i =>
			{
				var utc = solWinTrain_A[i].OpenTimeUtc;
				var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, tz);
				var d = ny.DayOfWeek;
				return d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;
			});

			var entryUtc = solWinTrain_A[entryIdx].OpenTimeUtc;

			// Находим индекс свечи, покрывающей baseline-exit, в B-сценарии,
			// и мутируем её close (это чистое будущее относительно entry).
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

			// Меняем будущий close только в сценарии B.
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

			var rowA = rowsA.SingleOrDefault (r => r.ToCausalDateUtc() == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.ToCausalDateUtc() == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// Таргет SolFwd1 ДОЛЖЕН измениться, мы специально поменяли future-close.
			Assert.NotEqual (rowA!.SolFwd1, rowB!.SolFwd1);

			// А вот фичи — нет. Это и есть запрет утечки.
			Assert.Equal (rowA.Causal.Features.Length, rowB.Causal.Features.Length);

			for (int i = 0; i < rowA.Causal.Features.Length; i++)
				{
				Assert.Equal (rowA.Causal.Features[i], rowB.Causal.Features[i], 10);
				}
			}
		}
	}
