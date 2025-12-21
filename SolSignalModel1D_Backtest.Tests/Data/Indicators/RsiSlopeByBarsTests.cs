using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using Xunit;
using CoreIndicators = SolSignalModel1D_Backtest.Core.Data.Indicators.Indicators;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	public sealed class RsiSlopeByBarsTests
		{
		[Fact]
		public void GetRsiSlopeByBars_UsesBarIndex_WhenTimeBasedKeyIsMissing ()
			{
			// Сценарий: "торговая" 6h-сетка без выходных.
			// Между Fri 18:00 и Mon 00:00 вырезаны все weekend-бары.
			// Time-based slope (openUtc - 6h*steps) попадёт в Sunday 18:00 (ключа нет),
			// а bar-based slope обязан взять idx-steps (существующий бар).
			var arr = new List<Candle6h>
				{
				Make6h (new DateTime (2025, 12, 12, 18, 0, 0, DateTimeKind.Utc)), // Fri 18:00
				Make6h (new DateTime (2025, 12, 15, 0, 0, 0, DateTimeKind.Utc)),  // Mon 00:00
				Make6h (new DateTime (2025, 12, 15, 6, 0, 0, DateTimeKind.Utc)),  // Mon 06:00
				Make6h (new DateTime (2025, 12, 15, 12, 0, 0, DateTimeKind.Utc)), // Mon 12:00 (target)
				};

			var rsi = new Dictionary<DateTime, double>
				{
				[arr[0].OpenTimeUtc] = 40.0,
				[arr[1].OpenTimeUtc] = 50.0,
				[arr[2].OpenTimeUtc] = 55.0,
				[arr[3].OpenTimeUtc] = 70.0,

				// Важно: НЕТ ключа 2025-12-14 18:00 (Sun 18:00),
				// чтобы time-based метод гарантированно падал.
				};

			const int idx = 3;
			const int steps = 3;

			// Time-based метод должен упасть, потому что попытается найти openUtc-18h = Sunday 18:00.
			Assert.Throws<InvalidOperationException> (() =>
				CoreIndicators.GetRsiSlope6h (rsi, arr[idx].OpenTimeUtc, steps));

			// Bar-based метод обязан отработать: requiredIdx = 0 (Fri 18:00).
			double slope = CoreIndicators.GetRsiSlopeByBars (rsi, arr, idx, steps, seriesKey: "RSI6h");
			double expected = (rsi[arr[idx].OpenTimeUtc] - rsi[arr[0].OpenTimeUtc]) / steps;

			Assert.True (Math.Abs (slope - expected) < 1e-12, $"slope={slope}, expected={expected}");
			}

		[Fact]
		public void GetRsiSlopeByBars_ReturnsNaN_OnWarmup ()
			{
			// Контракт: при idx < steps возвращается NaN как маркер warm-up.
			var arr = new List<Candle6h>
				{
				Make6h (new DateTime (2025, 12, 15, 0, 0, 0, DateTimeKind.Utc)),
				Make6h (new DateTime (2025, 12, 15, 6, 0, 0, DateTimeKind.Utc)),
				};

			var rsi = new Dictionary<DateTime, double>
				{
				[arr[0].OpenTimeUtc] = 50.0,
				[arr[1].OpenTimeUtc] = 55.0,
				};

			double slope = CoreIndicators.GetRsiSlopeByBars (rsi, arr, idx: 1, steps: 3, seriesKey: "RSI6h");
			Assert.True (double.IsNaN (slope), $"Expected NaN on warm-up, got {slope}");
			}

		[Fact]
		public void GetRsiSlopeByBars_Throws_WhenRequiredKeyMissing ()
			{
			// Если idx >= steps, но в RSI-словаре нет requiredFromUtc — это уже не warm-up,
			// а дырка/рассинхрон ключей -> fail-fast.
			var arr = new List<Candle6h>
				{
				Make6h (new DateTime (2025, 12, 12, 18, 0, 0, DateTimeKind.Utc)), // requiredIdx=0
				Make6h (new DateTime (2025, 12, 15, 0, 0, 0, DateTimeKind.Utc)),
				Make6h (new DateTime (2025, 12, 15, 6, 0, 0, DateTimeKind.Utc)),
				Make6h (new DateTime (2025, 12, 15, 12, 0, 0, DateTimeKind.Utc)), // idx=3
				};

			var rsi = new Dictionary<DateTime, double>
				{
				// Пропускаем ключ requiredFromUtc (arr[0]) специально.
				[arr[1].OpenTimeUtc] = 50.0,
				[arr[2].OpenTimeUtc] = 55.0,
				[arr[3].OpenTimeUtc] = 70.0,
				};

			var ex = Assert.Throws<InvalidOperationException> (() =>
				CoreIndicators.GetRsiSlopeByBars (rsi, arr, idx: 3, steps: 3, seriesKey: "RSI6h"));

			Assert.Contains ("slope precondition failed", ex.Message);
			}

		private static Candle6h Make6h ( DateTime openUtc )
			{
			// Для этих тестов OHLC значения не важны — важны только ключи OpenTimeUtc.
			// Цены ставятся валидными, чтобы не нарушать инварианты других утилит при возможном переиспользовании.
			return new Candle6h
				{
				OpenTimeUtc = openUtc,
				Open = 100.0,
				High = 101.0,
				Low = 99.0,
				Close = 100.0
				};
			}
		}
	}
