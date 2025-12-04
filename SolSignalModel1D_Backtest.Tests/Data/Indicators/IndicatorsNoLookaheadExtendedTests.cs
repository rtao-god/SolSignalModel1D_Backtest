using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using Xunit;
using CoreIndicators = SolSignalModel1D_Backtest.Core.Data.Indicators.Indicators;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	/// <summary>
	/// Расширенные тесты на отсутствие заглядывания вперёд у индикаторов:
	/// - FindNearest смотрит только назад;
	/// - DynVol не зависит от будущих свечей;
	/// - EMA не зависит от будущих свечей;
	/// - RsiSlope использует только прошлые RSI;
	/// - DXY 30d change не использует будущие точки;
	/// - FNG умеет корректно возвращать нейтральное значение.
	/// </summary>
	public sealed class IndicatorsNoLookaheadExtendedTests
		{
		private static Candle6h MakeCandle ( DateTime t, double close )
			{
			// Вспомогательный конструктор 6h-свечи:
			// close задаётся явно, остальные поля делаются консистентными.
			return new Candle6h
				{
				OpenTimeUtc = t,
				Open = close,
				Close = close,
				High = close + 1.0,
				Low = close - 1.0
				};
			}

		// ======================
		// FindNearest
		// ======================

		[Fact]
		public void FindNearest_DoesNotUseFutureValues ()
			{
			var baseTime = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var map = new Dictionary<DateTime, double>
			{
				{ baseTime, 10.0 },                 // вчера
                { baseTime.AddDays(2), 9999.0 }     // завтра (будущее относительно asOf)
            };

			var asOf = baseTime.AddDays (1); // даты в словаре нет

			// Ожидаем, что будет использовано только предыдущее значение (baseTime),
			// а будущее (baseTime+2) проигнорируется.
			double before = CoreIndicators.FindNearest (
				map,
				atUtc: asOf,
				defaultValue: -1.0,
				maxBackSteps: 2,
				step: TimeSpan.FromDays (1));

			Assert.Equal (10.0, before, 10);

			// Мутируем будущее значение — если FindNearest начнёт смотреть вперёд,
			// результат на asOf изменится и тест упадёт.
			map[baseTime.AddDays (2)] = -12345.0;

			double after = CoreIndicators.FindNearest (
				map,
				atUtc: asOf,
				defaultValue: -1.0,
				maxBackSteps: 2,
				step: TimeSpan.FromDays (1));

			Assert.Equal (before, after, 10);
			}

		// ======================
		// DynVol
		// ======================

		[Fact]
		public void DynVol6h_DoesNotDependOnFutureSamples ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h> ();

			// Строим 20 монотонных свечей.
			for (int i = 0; i < 20; i++)
				{
				var t = start.AddHours (6 * i);
				double close = 100.0 + i;
				arr.Add (MakeCandle (t, close));
				}

			int idx = 10;
			int lookback = 5;

			// DynVol в точке idx опирается только на прошлые окна.
			double before = CoreIndicators.ComputeDynVol6h (arr, idx, lookback);

			// Мутируем чистое будущее: свечи с индексами > idx.
			for (int i = idx + 1; i < arr.Count; i++)
				{
				arr[i].Close *= 100.0;
				arr[i].High *= 100.0;
				arr[i].Low /= 100.0;
				}

			double after = CoreIndicators.ComputeDynVol6h (arr, idx, lookback);

			Assert.Equal (before, after, 10);
			}

		[Fact]
		public void DynVol6h_ReturnsZero_WhenNotEnoughHistory ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h>
			{
				MakeCandle(start, 100.0)
			};

			// Недостаточно истории для расчёта среднего |ret| → ожидаем 0.
			double v = CoreIndicators.ComputeDynVol6h (arr, idx: 0, lookbackWindows: 5);

			Assert.Equal (0.0, v, 10);
			}

		// ======================
		// EMA
		// ======================

		[Fact]
		public void ComputeEma6h_DoesNotDependOnFutureSamples ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h> ();

			for (int i = 0; i < 40; i++)
				{
				var t = start.AddHours (6 * i);
				double close = 100.0 + i;
				arr.Add (MakeCandle (t, close));
				}

			const int period = 10;

			var emaBefore = CoreIndicators.ComputeEma6h (arr, period);
			var key = arr[25].OpenTimeUtc; // точка достаточно далеко от хвоста

			Assert.True (emaBefore.ContainsKey (key), "EMA должна быть посчитана для выбранного ключа.");
			double vBefore = emaBefore[key];

			// Мутируем будущие свечи: индексы > 30.
			for (int i = 30; i < arr.Count; i++)
				{
				arr[i].Close *= 100.0;
				arr[i].High *= 100.0;
				arr[i].Low /= 100.0;
				}

			var emaAfter = CoreIndicators.ComputeEma6h (arr, period);
			double vAfter = emaAfter[key];

			// Значения EMA в точке key не должны меняться.
			Assert.Equal (vBefore, vAfter, 10);
			}

		// ======================
		// RSI slope
		// ======================

		[Fact]
		public void GetRsiSlope6h_UsesOnlyPastRsiValues ()
			{
			// Имитируем готовую карту RSI по 3 точкам:
			// t0 (прошлое), t1 (asOf), t2 (будущее).
			var t0 = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var t1 = t0.AddDays (1);
			var t2 = t0.AddDays (2);

			var rsiMap = new Dictionary<DateTime, double>
			{
				{ t0, 40.0 },  // прошлое
                { t1, 60.0 },  // текущая точка
                { t2, 1_000.0 } // будущее, которое не должно участвовать
            };

			double slopeBefore = CoreIndicators.GetRsiSlope6h (
				rsiMap,
				asOfOpenUtc: t1,
				days: 1);

			// Ожидаемый наклон: 60 - 40 = 20.
			Assert.InRange (slopeBefore, 19.9, 20.1);

			// Меняем будущее значение — если GetRsiSlope6h начнёт смотреть вперёд
			// (через FindNearest или иначе), наклон изменится.
			rsiMap[t2] = -10_000.0;

			double slopeAfter = CoreIndicators.GetRsiSlope6h (
				rsiMap,
				asOfOpenUtc: t1,
				days: 1);

			Assert.Equal (slopeBefore, slopeAfter, 10);
			}

		// ======================
		// DXY 30d change
		// ======================

		[Fact]
		public void GetDxyChange30_DoesNotDependOnFutureSamples ()
			{
			var dxy = new Dictionary<DateTime, double> ();
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			// t0 и t0+30d задают "честную" 10%-ю доходность.
			var tPast = start;
			var tNow = start.AddDays (30);
			var tFuture = start.AddDays (40);

			dxy[tPast] = 100.0;
			dxy[tNow] = 110.0;
			dxy[tFuture] = 9999.0; // будущее, которое не должно влиять

			double changeBefore = CoreIndicators.GetDxyChange30 (dxy, tNow);

			Assert.InRange (changeBefore, 0.099, 0.101);

			// Сильно мутируем будущее значение.
			dxy[tFuture] = 1.0;

			double changeAfter = CoreIndicators.GetDxyChange30 (dxy, tNow);

			// Если реализация начнёт использовать будущие точки,
			// этот тест моментально сломается.
			Assert.Equal (changeBefore, changeAfter, 10);
			}

		// ======================
		// FNG: нейтральные значения
		// ======================

		[Fact]
		public void PickNearestFng_ReturnsNeutral_WhenNoHistory ()
			{
			var empty = new Dictionary<DateTime, double> ();
			var asOf = new DateTime (2024, 1, 10, 0, 0, 0, DateTimeKind.Utc);

			double val = CoreIndicators.PickNearestFng (empty, asOf);

			// По контракту функции без истории возвращается нейтральное значение 50.
			Assert.Equal (50.0, val, 10);
			}

		[Fact]
		public void PickNearestFng_ReturnsNeutral_WhenNoRecentHistoryWithin14Days ()
			{
			var asOf = new DateTime (2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);

			var fng = new Dictionary<DateTime, double>
			{
                // История слишком старая: 20 дней назад.
                { asOf.AddDays(-20), 10.0 }
			};

			double val = CoreIndicators.PickNearestFng (fng, asOf);

			// Источников в окне [-14d; 0] нет → функция должна вернуть 50 (нейтраль).
			Assert.Equal (50.0, val, 10);
			}
		}
	}
