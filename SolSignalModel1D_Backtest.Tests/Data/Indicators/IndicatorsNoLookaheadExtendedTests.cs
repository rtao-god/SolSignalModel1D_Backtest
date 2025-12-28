using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using Xunit;
using CoreIndicators = SolSignalModel1D_Backtest.Core.Causal.Data.Indicators.Indicators;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	/// <summary>
	/// Тесты на отсутствие lookahead в индикаторных утилитах.
	/// Идея одна и та же: зафиксировать результат в точке "as-of", затем изменить данные строго в будущем
	/// и убедиться, что значение в "as-of" не меняется.
	/// </summary>
	public sealed class IndicatorsNoLookaheadExtendedTests
		{
		private static Candle6h MakeCandle ( DateTime t, double close )
			{
			// Минимально консистентная свеча: OHLC вокруг close.
			// В тестах важно иметь валидные High/Low для функций, которые их читают.
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
			// Словарь содержит "прошлое" и "будущее", а для asOf отсутствует точный ключ.
			// Корректное поведение: брать ближайшее предыдущее значение, не заглядывая вперёд.
			var baseTime = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var map = new Dictionary<DateTime, double>
				{
					{ baseTime, 10.0 },                // прошлое
					{ baseTime.AddDays (2), 9999.0 }   // будущее относительно asOf
				};

			var asOf = baseTime.AddDays (1);

			double before = CoreIndicators.FindNearest (
				map,
				atUtc: asOf,
				defaultValue: -1.0,
				maxBackSteps: 2,
				step: TimeSpan.FromDays (1));

			Assert.Equal (10.0, before, 10);

			// Если реализация ошибочно использует будущее, изменение future-значения
			// изменит результат в asOf и тест упадёт.
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
			// DynVol рассчитывается как средний |ret| по окну, заканчивающемуся в idx.
			// Данные после idx не должны влиять на результат.
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h> ();

			for (int i = 0; i < 20; i++)
				{
				var t = start.AddHours (6 * i);
				double close = 100.0 + i;
				arr.Add (MakeCandle (t, close));
				}

			int idx = 10;
			int lookback = 5;

			double before = CoreIndicators.ComputeDynVol6h (arr, idx, lookback);

			// Искажаем только будущее (i > idx).
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
		public void DynVol6h_Throws_WhenNotEnoughHistory ()
			{
			// Строгий контракт: ComputeDynVol6h вызывается только после warm-up.
			// На первых элементах ряда (недостаток истории) должен быть fail-fast.
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var arr = new List<Candle6h>
				{
				MakeCandle (start, 100.0)
				};

			Assert.Throws<InvalidOperationException> (() =>
				CoreIndicators.ComputeDynVol6h (arr, idx: 0, lookbackWindows: 5));
			}

		// ======================
		// EMA
		// ======================

		[Fact]
		public void ComputeEma6h_DoesNotDependOnFutureSamples ()
			{
			// EMA в точке key зависит только от истории до этой точки.
			// Изменение хвоста (последующих свечей) не должно менять значение EMA на key.
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
			var key = arr[25].OpenTimeUtc;

			Assert.True (emaBefore.ContainsKey (key), "EMA должна быть рассчитана для выбранной точки.");
			double vBefore = emaBefore[key];

			// Искажаем только будущую часть ряда.
			for (int i = 30; i < arr.Count; i++)
				{
				arr[i].Close *= 100.0;
				arr[i].High *= 100.0;
				arr[i].Low /= 100.0;
				}

			var emaAfter = CoreIndicators.ComputeEma6h (arr, period);
			double vAfter = emaAfter[key];

			Assert.Equal (vBefore, vAfter, 10);
			}

        // ======================
        // RSI slope
        // ======================

        [Fact]
        public void GetRsiSlope6h_UsesOnlyPastRsiValues()
        {
            var tPast = new DateTime(2024, 1, 1, 18, 0, 0, DateTimeKind.Utc);
            var tNow = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            var tFuture = new DateTime(2024, 1, 2, 6, 0, 0, DateTimeKind.Utc);

            var rsiMap = new Dictionary<DateTime, double>
        {
            { tPast, 40.0 },
            { tNow, 60.0 },
            { tFuture, 1_000.0 }
        };

            double slopeBefore = CoreIndicators.GetRsiSlope6h(rsiMap, tNow, steps: 1);
            Assert.InRange(slopeBefore, 19.9, 20.1);

            rsiMap[tFuture] = -10_000.0;

            double slopeAfter = CoreIndicators.GetRsiSlope6h(rsiMap, tNow, steps: 1);
            Assert.Equal(slopeBefore, slopeAfter, 10);
        }

        // ======================
        // DXY 30d change
        // ======================

        [Fact]
		public void GetDxyChange30_DoesNotDependOnFutureSamples ()
			{
			// Для расчёта change30 нужны точки "now" и "past(=now-30d)".
			// Любые значения после now не должны влиять на результат.
			var dxy = new Dictionary<DateTime, double> ();
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var tPast = start;
			var tNow = start.AddDays (30);
			var tFuture = start.AddDays (40);

			dxy[tPast] = 100.0;
			dxy[tNow] = 110.0;
			dxy[tFuture] = 9999.0;

			double changeBefore = CoreIndicators.GetDxyChange30 (dxy, tNow);

			Assert.InRange (changeBefore, 0.099, 0.101);

			dxy[tFuture] = 1.0;

			double changeAfter = CoreIndicators.GetDxyChange30 (dxy, tNow);

			Assert.Equal (changeBefore, changeAfter, 10);
			}

		// ======================
		// FNG: строгие контракты
		// ======================

		[Fact]
		public void PickNearestFng_DoesNotUseFutureValue ()
			{
			var tPast = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var tAsOf = tPast.AddDays (1);
			var tFuture = tPast.AddDays (2);

			var fng = new Dictionary<DateTime, double>
				{
					{ tPast, 10.0 },
					{ tFuture, 99.0 }
				};

			double before = CoreIndicators.PickNearestFng (fng, tAsOf);
			Assert.Equal (10.0, before, 10);

			fng[tFuture] = -50.0;

			double after = CoreIndicators.PickNearestFng (fng, tAsOf);
			Assert.Equal (before, after, 10);
			}

		[Fact]
		public void PickNearestFng_Throws_WhenNoHistory ()
			{
			// Пустая серия FNG означает отсутствие источника данных.
			// Строгий контракт: это невалидное состояние.
			var empty = new Dictionary<DateTime, double> ();
			var asOf = new DateTime (2024, 1, 10, 0, 0, 0, DateTimeKind.Utc);

			Assert.Throws<InvalidOperationException> (() => CoreIndicators.PickNearestFng (empty, asOf));
			}

		[Fact]
		public void PickNearestFng_Throws_WhenNoRecentHistoryWithin14Days ()
			{
			// Серия есть, но в допустимом lookback [-14d..0] нет точек.
			// Это рассматривается как нарушение coverage-guard'ов.
			var asOf = new DateTime (2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);

			var fng = new Dictionary<DateTime, double>
				{
					{ asOf.AddDays (-20), 10.0 }
				};

			Assert.Throws<InvalidOperationException> (() => CoreIndicators.PickNearestFng (fng, asOf));
			}
		}
	}
