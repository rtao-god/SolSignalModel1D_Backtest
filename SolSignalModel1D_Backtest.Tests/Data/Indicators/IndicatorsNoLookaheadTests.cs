using System;
using System.Collections.Generic;
using Xunit;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using CoreIndicators = SolSignalModel1D_Backtest.Core.Data.Indicators.Indicators;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	/// <summary>
	/// Строгие инварианты на индикаторах:
	/// - Ret6h для индекса i не зависит от будущих свечей j > i;
	/// - ATR(i) не зависит от будущих значений;
	/// - RSI(i) не зависит от будущих значений.
	/// </summary>
	public sealed class IndicatorsNoLookaheadTests
		{
		private static Candle6h MakeCandle ( DateTime t, double close )
			{
			return new Candle6h
				{
				OpenTimeUtc = t,
				Close = close,
				High = close + 1.0,
				Low = close - 1.0,
				};
			}

		[Fact]
		public void Ret6h_DoesNotDependOnFutureCandles ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h> ();

			for (int i = 0; i < 10; i++)
				{
				var t = start.AddHours (6 * i);
				arr.Add (MakeCandle (t, 100.0 + i));
				}

			// Берём ретурн на индексе 5 относительно 1 и 3 окон назад.
			double r1_before = CoreIndicators.Ret6h (arr, idx: 5, windowsBack: 1);
			double r3_before = CoreIndicators.Ret6h (arr, idx: 5, windowsBack: 3);

			// Мутируем СОВСЕМ будущее: свечу с индексом 9.
			arr[9].Close = 9999.0;
			arr[9].High = 10000.0;
			arr[9].Low = 9998.0;

			double r1_after = CoreIndicators.Ret6h (arr, idx: 5, windowsBack: 1);
			double r3_after = CoreIndicators.Ret6h (arr, idx: 5, windowsBack: 3);

			// Для корректной реализации изменения в будущем не должны влиять.
			Assert.Equal (r1_before, r1_after, 10);
			Assert.Equal (r3_before, r3_after, 10);
			}

		[Fact]
		public void ComputeAtr6h_DoesNotDependOnFutureSamples ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h> ();

			for (int i = 0; i < 12; i++)
				{
				var t = start.AddHours (6 * i);
				double close = 100.0 + i;
				arr.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = close,
					High = close + 2.0,
					Low = close - 2.0
					});
				}

			const int period = 5;

			var atrBefore = CoreIndicators.ComputeAtr6h (arr, period);
			var key = arr[7].OpenTimeUtc; // i = 7 гарантированно после первого ATR

			Assert.True (atrBefore.ContainsKey (key), "ATR должен быть посчитан для ключа 7-й свечи.");
			double vBefore = atrBefore[key];

			// Меняем самую ПОСЛЕДНЮЮ свечу (индекс 11) — это чистое будущее для точки с key.
			arr[11].High += 1000;
			arr[11].Low -= 1000;
			arr[11].Close += 500;

			var atrAfter = CoreIndicators.ComputeAtr6h (arr, period);
			double vAfter = atrAfter[key];

			Assert.Equal (vBefore, vAfter, 10);
			}

		[Fact]
		public void ComputeRsi6h_DoesNotDependOnFutureSamples ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var arr = new List<Candle6h> ();

			for (int i = 0; i < 25; i++)
				{
				var t = start.AddHours (6 * i);
				double close = 100.0 + Math.Sin (i / 3.0) * 5.0;
				arr.Add (MakeCandle (t, close));
				}

			const int period = 14;

			var rsiBefore = CoreIndicators.ComputeRsi6h (arr, period);
			var key = arr[period + 2].OpenTimeUtc; // достаточно далеко от хвоста

			Assert.True (rsiBefore.ContainsKey (key), "RSI должен быть посчитан для выбранного ключа.");
			double vBefore = rsiBefore[key];

			// Мутируем последнюю свечу — 100% будущее для уровня period+2.
			arr[^1].Close += 100;
			arr[^1].High += 100;
			arr[^1].Low += 100;

			var rsiAfter = CoreIndicators.ComputeRsi6h (arr, period);
			double vAfter = rsiAfter[key];

			Assert.Equal (vBefore, vAfter, 10);
			}
		}
	}
