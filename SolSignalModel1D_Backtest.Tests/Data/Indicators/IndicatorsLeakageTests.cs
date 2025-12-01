using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	/// <summary>
	/// Структурный тест:
	/// фичи DataRow и Label для "ранних" дней не должны меняться,
	/// если мутировать макро-ряды (FNG / DXY / PAXG) СИЛЬНО в будущем.
	///
	/// Если какая-то индикаторная функция смотрит в будущее
	/// (например, использует будущие даты DXY/FNG для дня T),
	/// этот тест должен упасть.
	/// </summary>
	public sealed class IndicatorsLeakageTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		[Fact]
		public void Features_DoNotChange_WhenFutureMacroSeriesAreMutated ()
			{
			// Берём тот же стиль сетапа, что и в RowBuilderFeatureLeakageTests:
			// длинная 6h-история, чуть более 300 окон.

			const int total6h = 300;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);
			var tz = NyTz; // в тестах достаточно UTC

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

			// Для простоты считаем train-окном всю историю.
			var solWinTrain = solAll6h;
			var btcWinTrain = btcAll6h;
			var paxgWinTrain = paxgAll6h;

			// 1m-серия: сплошные минуты, чтобы PathLabeler и MinMove могли работать.
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

			// Базовые FNG/DXY ряды: "ровные" по датам, без пропусков.
			var fngBase = new Dictionary<DateTime, int> ();
			var dxyBase = new Dictionary<DateTime, double> ();

			var firstDate = start.Date.AddDays (-120);
			var lastDate = start.Date.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				// ВАЖНО: Kind = Utc, чтобы совпадать с openUtc.Date.
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fngBase[key] = 50;
				dxyBase[key] = 100.0;
				}

			// ExtraDaily не используем.
			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			// === A-сценарий: макро без мутаций ===
			var rowsA = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: extraDaily,
				nyTz: tz);

			// Берём какой-нибудь отметочный день из середины истории,
			// чтобы гарантированно было и достаточно прошлого, и достаточно будущего.
			Assert.True (rowsA.Count > 50, "rowsA слишком мало для теста");
			var cutoff = rowsA[rowsA.Count / 3].Date;

			// === B-сценарий: копия FNG/DXY, но будущая часть сильно мутирована ===
			var fngB = new Dictionary<DateTime, int> (fngBase);
			var dxyB = new Dictionary<DateTime, double> (dxyBase);

			// Сдвигаем "будущее" после cutoff + 10 дней.
			var mutateFrom = cutoff.Date.AddDays (10);

			foreach (var key in fngB.Keys.ToList ())
				{
				if (key > mutateFrom)
					{
					// Нарочно делаем абсурдные значения, чтобы эффект был заметен,
					// если вдруг индикаторы смотрят вперёд.
					fngB[key] = fngB[key] + 40;
					}
				}

			foreach (var key in dxyB.Keys.ToList ())
				{
				if (key > mutateFrom)
					{
					dxyB[key] = dxyB[key] * 10.0;
					}
				}

			var rowsB = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngB,
				dxySeries: dxyB,
				extraDaily: extraDaily,
				nyTz: tz);

			// Сопоставляем строки по дате.
			var dictA = rowsA.ToDictionary (r => r.Date, r => r);
			var dictB = rowsB.ToDictionary (r => r.Date, r => r);

			// Для всех дат <= cutoff:
			// - Label должен быть одинаковым;
			// - Features должны совпадать с точностью до double-шума.
			foreach (var kv in dictA)
				{
				var date = kv.Key;
				if (date > cutoff)
					continue;

				Assert.True (dictB.ContainsKey (date), $"Во втором наборе нет строки для {date:O}");

				var a = kv.Value;
				var b = dictB[date];

				Assert.Equal (a.Label, b.Label);

				Assert.Equal (a.Features.Length, b.Features.Length);
				for (int i = 0; i < a.Features.Length; i++)
					{
					Assert.Equal (a.Features[i], b.Features[i], 10);
					}
				}
			}
		}
	}
