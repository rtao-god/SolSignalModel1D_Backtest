using System;
using System.Collections.Generic;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;

namespace SolSignalModel1D_Backtest.Tests.ML.SL
	{
	/// <summary>
	/// Тесты каузальности SlFeatureBuilder:
	/// 1) фичи не должны зависеть от 1h свечей на/после entryUtc;
	/// 2) если entryUtc не на границе часа, свеча текущего часа (ещё не закрытая)
	///    не должна попадать в фичи.
	/// </summary>
	public sealed class SlFeatureBuilderFutureBlindTests
		{
		[Fact]
		public void Features_DoNotChange_WhenCandlesAfterEntryAreMutated ()
			{
			var entryUtc = new DateTime (2020, 2, 24, 6, 0, 0, DateTimeKind.Utc);

			var candles = BuildHourlySeries (
				fromUtc: entryUtc.AddHours (-12),
				toUtc: entryUtc.AddHours (+12),
				basePrice: 100.0);

			var featsA = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: true,
				strongSignal: true,
				dayMinMove: 0.03,
				entryPrice: 100.0,
				candles1h: candles);

			// Мутируем будущее: все бары с OpenTimeUtc >= entryUtc превращаем в "ракеты".
			var candlesB = Clone (candles);
			foreach (var c in candlesB)
				{
				if (c.OpenTimeUtc >= entryUtc)
					{
					c.Open *= 10.0;
					c.Close *= 10.0;
					c.High *= 10.0;
					c.Low *= 10.0;
					}
				}

			var featsB = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: true,
				strongSignal: true,
				dayMinMove: 0.03,
				entryPrice: 100.0,
				candles1h: candlesB);

			AssertEqualFloatArrays (featsA, featsB);
			}

		[Fact]
		public void Features_DoNotUse_NotClosedHour_WhenEntryIsMidHour ()
			{
			// entryUtc внутри часа: 06:30.
			// Свеча 06:00–07:00 на момент входа НЕ закрыта и не должна влиять на фичи.
			var entryUtc = new DateTime (2020, 2, 24, 6, 30, 0, DateTimeKind.Utc);

			var candles = BuildHourlySeries (
				fromUtc: new DateTime (2020, 2, 24, 0, 0, 0, DateTimeKind.Utc),
				toUtc: new DateTime (2020, 2, 24, 12, 0, 0, DateTimeKind.Utc),
				basePrice: 100.0);

			var featsA = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: false,
				strongSignal: false,
				dayMinMove: 0.02,
				entryPrice: 100.0,
				candles1h: candles);

			// Мутируем "текущий" часовой бар 06:00, который не должен участвовать.
			var candlesB = Clone (candles);
			for (int i = 0; i < candlesB.Count; i++)
				{
				if (candlesB[i].OpenTimeUtc == new DateTime (2020, 2, 24, 6, 0, 0, DateTimeKind.Utc))
					{
					candlesB[i].Open *= 50.0;
					candlesB[i].Close *= 50.0;
					candlesB[i].High *= 50.0;
					candlesB[i].Low *= 50.0;
					}
				}

			var featsB = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: false,
				strongSignal: false,
				dayMinMove: 0.02,
				entryPrice: 100.0,
				candles1h: candlesB);

			AssertEqualFloatArrays (featsA, featsB);
			}

		private static List<Candle1h> BuildHourlySeries ( DateTime fromUtc, DateTime toUtc, double basePrice )
			{
			if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ("Test series must be UTC.");
			if (toUtc <= fromUtc)
				throw new InvalidOperationException ("toUtc must be > fromUtc.");

			var res = new List<Candle1h> ();
			var t = fromUtc;

			int k = 0;
			while (t <= toUtc)
				{
				double p = basePrice + k * 0.1;

				res.Add (new Candle1h
					{
					OpenTimeUtc = t,
					Open = p,
					Close = p + 0.05,
					High = p + 0.10,
					Low = p - 0.10
					});

				t = t.AddHours (1);
				k++;
				}

			return res;
			}

		private static List<Candle1h> Clone ( List<Candle1h> src )
			{
			var res = new List<Candle1h> (src.Count);
			foreach (var c in src)
				{
				res.Add (new Candle1h
					{
					OpenTimeUtc = c.OpenTimeUtc,
					Open = c.Open,
					High = c.High,
					Low = c.Low,
					Close = c.Close
					});
				}
			return res;
			}

		private static void AssertEqualFloatArrays ( float[] a, float[] b )
			{
			Assert.NotNull (a);
			Assert.NotNull (b);
			Assert.Equal (a.Length, b.Length);

			for (int i = 0; i < a.Length; i++)
				{
				Assert.True (
					a[i].Equals (b[i]),
					$"Mismatch at idx={i}: a={a[i]}, b={b[i]}");
				}
			}
		}
	}
