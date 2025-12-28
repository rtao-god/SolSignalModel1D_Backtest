using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Candles
	{
	public sealed class CandleNdjsonStoreTests
		{
		[Fact]
		public void ReadRange_RespectsStartInclusive_EndExclusive ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "candles.ndjson");

				var t0 = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				var t1 = t0.AddMinutes (1);
				var t2 = t0.AddMinutes (2);

				File.WriteAllLines (path, new[]
					{
					Line (t0, 1.0),
					Line (t1, 2.0),
					Line (t2, 3.0)
					});

				var store = new CandleNdjsonStore (path);

				var slice = store.ReadRange (t1, t2);
				Assert.Single (slice);
				Assert.Equal (t1, slice[0].OpenTimeUtc);

				var all = store.ReadRange (t0, t2.AddMinutes (1));
				Assert.Equal (3, all.Count);
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		[Fact]
		public void ReadRange_Throws_OnNonStrictOrder ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "candles.ndjson");

				var t0 = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				var t1 = t0.AddMinutes (1);

				File.WriteAllLines (path, new[]
					{
					Line (t1, 2.0),
					Line (t0, 1.0)
					});

				var store = new CandleNdjsonStore (path);

				var ex = Assert.Throws<InvalidOperationException> (() =>
					store.ReadRange (t0, t1.AddMinutes (1)));

				Assert.Contains ("not strictly increasing", ex.Message, StringComparison.OrdinalIgnoreCase);
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		[Fact]
		public void ReadRange_Throws_OnMissingOhlcField ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "candles.ndjson");
				var t0 = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

				var json = JsonSerializer.Serialize (new
					{
					t = t0.ToString ("O"),
					h = 1.0,
					l = 1.0,
					c = 1.0
					});

				File.WriteAllLines (path, new[] { json });

				var store = new CandleNdjsonStore (path);

				var ex = Assert.Throws<InvalidOperationException> (() =>
					store.ReadRange (t0, t0.AddMinutes (1)));

				Assert.Contains ("o/h/l/c", ex.Message, StringComparison.OrdinalIgnoreCase);
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		[Fact]
		public void TryGetFirstAndLastTimestampUtc_SkipsEmptyLines ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "candles.ndjson");

				var t0 = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				var t1 = t0.AddMinutes (1);

				File.WriteAllLines (path, new[]
					{
					"",
					Line (t0, 1.0),
					"   ",
					Line (t1, 2.0)
					});

				var store = new CandleNdjsonStore (path);

				Assert.Equal (t0, store.TryGetFirstTimestampUtc ());
				Assert.Equal (t1, store.TryGetLastTimestampUtc ());
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		private static string Line ( DateTime t, double price )
			{
			return JsonSerializer.Serialize (new
				{
				t = t.ToString ("O"),
				o = price,
				h = price,
				l = price,
				c = price
				});
			}

		private static string CreateTempDir ()
			{
			var dir = Path.Combine (Path.GetTempPath (), "ssm-tests", Guid.NewGuid ().ToString ("N"));
			Directory.CreateDirectory (dir);
			return dir;
			}

		private static void TryDeleteDir ( string dir )
			{
			try
				{
				if (Directory.Exists (dir))
					Directory.Delete (dir, recursive: true);
				}
			catch
				{
				// ошибки очистки не критичны для теста
				}
			}
		}

	public sealed class CandleResamplerTests
		{
		[Fact]
		public void ResampleTo6h_AggregatesByAnchorAndPreservesOpenClose ()
			{
			var start = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var src = new List<CandleNdjsonStore.CandleLine> ();

			for (int i = 0; i <= 6; i++)
				{
				var t = start.AddHours (i);
				double open = 100.0 + i;
				double close = open + 0.5;
				double high = open + 1.0;
				double low = open - 1.0;

				src.Add (new CandleNdjsonStore.CandleLine (t, open, high, low, close));
				}

			src = src.OrderByDescending (x => x.OpenTimeUtc).ToList ();

			var res = InvokeResampleTo6h (src, sourceIsOneHour: true);

			Assert.Equal (2, res.Count);

			var first = res[0];
			Assert.Equal (start, first.OpenTimeUtc);
			Assert.Equal (100.0, first.Open, 10);
			Assert.Equal (106.0, first.High, 10);
			Assert.Equal (99.0, first.Low, 10);
			Assert.Equal (105.5, first.Close, 10);

			var second = res[1];
			Assert.Equal (start.AddHours (6), second.OpenTimeUtc);
			Assert.Equal (106.0, second.Open, 10);
			Assert.Equal (107.0, second.High, 10);
			Assert.Equal (105.0, second.Low, 10);
			Assert.Equal (106.5, second.Close, 10);
			}

		private static List<CandleNdjsonStore.CandleLine> InvokeResampleTo6h (
			List<CandleNdjsonStore.CandleLine> src,
			bool sourceIsOneHour )
			{
			var mi = typeof (CandleResampler)
				.GetMethod ("ResampleTo6h", BindingFlags.NonPublic | BindingFlags.Static);

			if (mi == null)
				throw new InvalidOperationException ("ResampleTo6h method not found via reflection.");

			try
				{
				return (List<CandleNdjsonStore.CandleLine>) mi.Invoke (null, new object[] { src, sourceIsOneHour })!;
				}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
				throw ex.InnerException;
				}
			}
		}
	}
