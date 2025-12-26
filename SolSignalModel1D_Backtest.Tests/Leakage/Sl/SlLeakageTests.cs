using System;
using System.Collections.Generic;
using Xunit;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.SL;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Sl
	{
	/// <summary>
	/// Sanity-тесты для SL-фич:
	/// ключевой инвариант — отсутствие доступа к будущим 1h-свечам.
	/// </summary>
	public class SlLeakageTests
		{
		[Fact]
		public void SlFeatureBuilder_Build_IgnoresFutureCandles ()
			{
			// Arrange: 6 часов истории до entry и ещё 3 часа после entry.
			var entryUtc = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

			var past = new List<Candle1h> ();
			for (int i = 6; i > 0; i--)
				{
				var open = entryUtc.AddHours (-i);
				past.Add (new Candle1h
					{
					OpenTimeUtc = open,
					Open = 100,
					High = 101,
					Low = 99,
					Close = 100.5
					});
				}

			var future = new List<Candle1h> ();
			for (int i = 0; i < 3; i++)
				{
				var open = entryUtc.AddHours (i);
				future.Add (new Candle1h
					{
					OpenTimeUtc = open,
					Open = 10_000,   // заведомо другие значения
					High = 20_000,
					Low = 5_000,
					Close = 15_000
					});
				}

			var onlyPast = past;
			var pastAndFuture = new List<Candle1h> ();
			pastAndFuture.AddRange (past);
			pastAndFuture.AddRange (future);

			// Act: считаем фичи сначала только по прошлым свечам,
			// затем по "прошлое + будущее".
			var featsPast = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: true,
				strongSignal: true,
				dayMinMove: 0.03,
				entryPrice: 100,
				candles1h: onlyPast);

			var featsWithFuture = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: true,
				strongSignal: true,
				dayMinMove: 0.03,
				entryPrice: 100,
				candles1h: pastAndFuture);

			// Assert: добавление будущих свечей не меняет вектор фич.
			Assert.Equal (featsPast.Length, featsWithFuture.Length);
			for (int i = 0; i < featsPast.Length; i++)
				{
				Assert.Equal (featsPast[i], featsWithFuture[i]);
				}
			}

		[Fact]
		public void SlFeatureBuilder_Build_HandlesEmptyHistory ()
			{
			// Arrange: нет 1h-истории, entryPrice > 0.
			var entryUtc = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

			// Act: должен вернуть вектор нужной длины без исключений.
			var feats = SlFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: true,
				strongSignal: false,
				dayMinMove: 0.03,
				entryPrice: 100,
				candles1h: new List<Candle1h> ());

			// Assert
			Assert.NotNull (feats);
			Assert.True (feats.Length > 0);
			}
		}
	}
