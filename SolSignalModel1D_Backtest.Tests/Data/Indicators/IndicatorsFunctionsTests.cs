using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	/// <summary>
	/// Тесты для "чистых" индикаторных утилит без I/O:
	/// ATR/RSI/SMA/EMA, FNG/DXY-вспомогательные функции.
	/// </summary>
	public sealed class IndicatorsFunctionsTests
		{
		private static List<Candle6h> BuildTrendingSeries ( int count, double startPrice, double step )
			{
			// Простая генерация 6h-рядов:
			// цена монотонно растёт или падает с заданным шагом.
			var list = new List<Candle6h> (count);
			var t = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			double price = startPrice;

			for (int i = 0; i < count; i++)
				{
				double open = price;
				double close = price + step;
				double high = Math.Max (open, close) * 1.01;
				double low = Math.Min (open, close) * 0.99;

				list.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = open,
					High = high,
					Low = low,
					Close = close
					});

				price = close;
				t = t.AddHours (6);
				}

			return list;
			}

		[Fact]
		public void Ret6h_ReturnsNaN_WhenNotEnoughHistory ()
			{
			var arr = BuildTrendingSeries (1, 100.0, 1.0);

			// Для idx=0 и windowsBack=1 нет достаточной истории.
			double ret = CoreIndicators.Ret6h (arr, 0, 1);

			Assert.True (double.IsNaN (ret));
			}

		[Fact]
		public void ComputeAtr6h_ProducesPositiveValues_OnTrendingSeries ()
			{
			var arr = BuildTrendingSeries (50, 100.0, 2.0);

			var atr = Indicators.ComputeAtr6h (arr, period: 14);

			// Должны быть какие-то значения и все они > 0.
			Assert.NotEmpty (atr);
			Assert.All (atr.Values, v => Assert.True (v > 0.0));
			}

		[Fact]
		public void ComputeRsi6h_RespondsToTrendDirection ()
			{
			// Ап-тренд: цена растёт, RSI должен быть > 50.
			var up = BuildTrendingSeries (50, 100.0, +2.0);
			var rsiUp = CoreIndicators.ComputeRsi6h (up, period: 14);
			double lastRsiUp = rsiUp.Values.Last ();

			// Даун-тренд: цена падает, RSI должен быть < 50.
			var down = BuildTrendingSeries (50, 200.0, -2.0);
			var rsiDown = CoreIndicators.ComputeRsi6h (down, period: 14);
			double lastRsiDown = rsiDown.Values.Last ();

			Assert.InRange (lastRsiUp, 50.0, 100.0);
			Assert.InRange (lastRsiDown, 0.0, 50.0);
			Assert.True (lastRsiUp > lastRsiDown);
			}

		[Fact]
		public void ComputeEma6h_FollowsPriceDynamics ()
			{
			var arr = BuildTrendingSeries (30, 100.0, 1.0);

			var ema = CoreIndicators.ComputeEma6h (arr, period: 10);

			// Проверяем, что EMA существует и последняя EMA ближе к последней цене, чем к первой.
			Assert.NotEmpty (ema);

			double lastPrice = arr.Last ().Close;
			double firstPrice = arr.First ().Close;
			double lastEma = ema.Values.Last ();

			double distToLast = Math.Abs (lastEma - lastPrice);
			double distToFirst = Math.Abs (lastEma - firstPrice);

			Assert.True (distToLast < distToFirst);
			}

		[Fact]
		public void PickNearestFng_UsesPreviousDay_WhenExactMissing ()
			{
			var fng = new Dictionary<DateTime, double>
				{
					{ new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10 },
					{ new DateTime (2024, 1, 3, 0, 0, 0, DateTimeKind.Utc), 20 }
				};

			// На 2024-01-02 нет значения, ожидаем взять 2024-01-01.
			var asOf = new DateTime (2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);

			double val = CoreIndicators.PickNearestFng (fng, asOf);

			Assert.Equal (10, val);
			}

		[Fact]
		public void GetDxyChange30_ComputesRelativeChange ()
			{
			var dxy = new Dictionary<DateTime, double> ();
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			// Через 30 дней DXY вырастает ровно на 10%.
			dxy[start] = 100.0;
			dxy[start.AddDays (30)] = 110.0;

			double change = CoreIndicators.GetDxyChange30 (dxy, start.AddDays (30));

			// Ожидается прирост ~0.10.
			Assert.InRange (change, 0.099, 0.101);
			}
		}
	}
