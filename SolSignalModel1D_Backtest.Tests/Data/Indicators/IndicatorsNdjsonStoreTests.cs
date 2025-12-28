using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Causal.Data.Indicators;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	public sealed class IndicatorsNdjsonStoreTests
		{
		[Fact]
		public void ReadRange_IncludesStartAndEndDates ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "fng.ndjson");

				var d1 = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				var d2 = d1.AddDays (1);
				var d3 = d1.AddDays (2);

				File.WriteAllLines (path, new[]
					{
					Line (d1, 10.0),
					Line (d2, 20.0),
					Line (d3, 30.0)
					});

				var store = new IndicatorsNdjsonStore (path);

				var res = store.ReadRange (d2, d3);

				Assert.Equal (2, res.Count);
				Assert.True (res.ContainsKey (d2));
				Assert.True (res.ContainsKey (d3));
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		[Fact]
		public void ReadRange_Throws_OnMissingValue ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "fng.ndjson");

				var json = JsonSerializer.Serialize (new
					{
					d = "2024-01-01"
					});

				File.WriteAllLines (path, new[] { json });

				var store = new IndicatorsNdjsonStore (path);

				var ex = Assert.Throws<InvalidOperationException> (() =>
					store.ReadRange (new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
						new DateTime (2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

				Assert.Contains ("property 'v' not found", ex.Message, StringComparison.OrdinalIgnoreCase);
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		[Fact]
		public void ReadRange_Throws_OnInvalidDayFormat ()
			{
			var dir = CreateTempDir ();
			try
				{
				var path = Path.Combine (dir, "fng.ndjson");

				var json = JsonSerializer.Serialize (new
					{
					d = "2024/01/01",
					v = 10.0
					});

				File.WriteAllLines (path, new[] { json });

				var store = new IndicatorsNdjsonStore (path);

				var ex = Assert.Throws<InvalidOperationException> (() =>
					store.ReadRange (new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
						new DateTime (2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

				Assert.Contains ("cannot parse day", ex.Message, StringComparison.OrdinalIgnoreCase);
				}
			finally
				{
				TryDeleteDir (dir);
				}
			}

		private static string Line ( DateTime dayUtc, double value )
			{
			return JsonSerializer.Serialize (new
				{
				d = dayUtc.ToString ("yyyy-MM-dd"),
				v = value
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
	}
