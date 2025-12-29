using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Candles
	{
	public sealed class CandleReaderLeakageTests
		{
		[Fact]
		public void ReadAll1m_ReturnsExactPrices_FromNdjson ()
			{
			var symbol = "TESTLEAK1M" + Guid.NewGuid ().ToString ("N").Substring (0, 8);
			var path = CandlePaths.File (symbol, "1m");

			WriteCandles (path, new[]
				{
				Line ("2024-03-01T00:00:00Z", 100, 110, 90, 105),
				Line ("2024-03-01T00:01:00Z", 200, 210, 190, 205),
				Line ("2024-03-01T00:02:00Z", 300, 310, 290, 305),
				});

			try
				{
				var candles = InvokeReadAll1m (symbol);

				Assert.Equal (3, candles.Count);
				Assert.Equal (100, candles[0].Open);
				Assert.Equal (200, candles[1].Open);
				Assert.Equal (300, candles[2].Open);
				Assert.Equal (105, candles[0].Close);
				Assert.Equal (205, candles[1].Close);
				Assert.Equal (305, candles[2].Close);
				}
			finally
				{
				TryDeleteFile (path);
				}
			}

		[Fact]
		public void ReadAll1mWeekends_ReturnsExactPrices_FromNdjson ()
			{
			var symbol = "TESTLEAK1MW" + Guid.NewGuid ().ToString ("N").Substring (0, 8);
			var path = CandlePaths.WeekendFile (symbol, "1m");

			WriteCandles (path, new[]
				{
				Line ("2024-03-02T00:00:00Z", 111, 112, 110, 111.5),
				Line ("2024-03-02T00:01:00Z", 222, 223, 221, 222.5),
				});

			try
				{
				var candles = InvokeReadAll1mWeekends (symbol);

				Assert.Equal (2, candles.Count);
				Assert.Equal (111, candles[0].Open);
				Assert.Equal (222, candles[1].Open);
				Assert.Equal (111.5, candles[0].Close);
				Assert.Equal (222.5, candles[1].Close);
				}
			finally
				{
				TryDeleteFile (path);
				}
			}

		private static void WriteCandles ( string path, IReadOnlyList<CandleNdjsonStore.CandleLine> lines )
			{
			if (lines == null) throw new ArgumentNullException (nameof (lines));
			if (lines.Count == 0) throw new ArgumentException ("lines must be non-empty.", nameof (lines));

			if (File.Exists (path))
				File.Delete (path);

			var store = new CandleNdjsonStore (path);
			store.Append (lines);
			}

		private static CandleNdjsonStore.CandleLine Line (
			string isoUtc,
			double open,
			double high,
			double low,
			double close )
			{
			var t = DateTime.Parse (isoUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
			return new CandleNdjsonStore.CandleLine (t, open, high, low, close);
			}

		private static List<Candle1m> InvokeReadAll1m ( string symbol )
			{
			var mi = typeof (global::SolSignalModel1D_Backtest.Program)
				.GetMethod ("ReadAll1m", BindingFlags.NonPublic | BindingFlags.Static);

			if (mi == null)
				throw new InvalidOperationException ("ReadAll1m method not found via reflection.");

			try
				{
				return (List<Candle1m>) mi.Invoke (null, new object[] { symbol })!;
				}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
				throw ex.InnerException;
				}
			}

		private static List<Candle1m> InvokeReadAll1mWeekends ( string symbol )
			{
			var mi = typeof (global::SolSignalModel1D_Backtest.Program)
				.GetMethod ("ReadAll1mWeekends", BindingFlags.NonPublic | BindingFlags.Static);

			if (mi == null)
				throw new InvalidOperationException ("ReadAll1mWeekends method not found via reflection.");

			try
				{
				return (List<Candle1m>) mi.Invoke (null, new object[] { symbol })!;
				}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
				throw ex.InnerException;
				}
			}

		private static void TryDeleteFile ( string path )
			{
			try
				{
				if (File.Exists (path))
					File.Delete (path);
				}
			catch
				{
				}
			}
		}
	}
