using System;
using SolSignalModel1D_Backtest.Core.Causal.Data.Indicators;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Indicators
	{
	public sealed class IndicatorsDailyUpdaterLeakageTests
		{
		[Fact]
		public void LoadFngDict_ReturnsExactValues_NoForwardShift ()
			{
			var tmp = CreateTempDir ();
			try
				{
				WriteIndicatorFile (
					path: Path.Combine (tmp, "fng.ndjson"),
					new[]
						{
						("2024-01-02", 10d),
						("2024-01-03", 20d),
						("2024-01-04", 30d),
						});

				using var http = new HttpClient ();
				var updater = new IndicatorsDailyUpdater (http, tmp);

				var start = ParseUtcDay ("2024-01-02");
				var end = ParseUtcDay ("2024-01-04");
				var dict = updater.LoadFngDict (start, end);

				Assert.Equal (10d, dict[ParseUtcDay ("2024-01-02")]);
				Assert.Equal (20d, dict[ParseUtcDay ("2024-01-03")]);
				Assert.Equal (30d, dict[ParseUtcDay ("2024-01-04")]);
				}
			finally
				{
				TryDeleteDir (tmp);
				}
			}

		[Fact]
		public void LoadDxyDict_ReturnsExactValues_NoForwardShift ()
			{
			var tmp = CreateTempDir ();
			try
				{
				WriteIndicatorFile (
					path: Path.Combine (tmp, "dxy.ndjson"),
					new[]
						{
						("2024-02-05", 101.1d),
						("2024-02-06", 102.2d),
						("2024-02-07", 103.3d),
						});

				using var http = new HttpClient ();
				var updater = new IndicatorsDailyUpdater (http, tmp);

				var start = ParseUtcDay ("2024-02-05");
				var end = ParseUtcDay ("2024-02-07");
				var dict = updater.LoadDxyDict (start, end);

				Assert.Equal (101.1d, dict[ParseUtcDay ("2024-02-05")]);
				Assert.Equal (102.2d, dict[ParseUtcDay ("2024-02-06")]);
				Assert.Equal (103.3d, dict[ParseUtcDay ("2024-02-07")]);
				}
			finally
				{
				TryDeleteDir (tmp);
				}
			}

		private static void WriteIndicatorFile ( string path, IReadOnlyList<(string DayIso, double Value)> rows )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (rows.Count == 0) throw new ArgumentException ("rows must be non-empty.", nameof (rows));

			var store = new IndicatorsNdjsonStore (path);
			var lines = rows.Select (r =>
				new IndicatorsNdjsonStore.IndicatorLine (ParseUtcDay (r.DayIso), r.Value));

			store.OverwriteAtomic (lines);
			}

		private static DateTime ParseUtcDay ( string isoDate )
			{
			var dt = DateTime.Parse (isoDate, null, System.Globalization.DateTimeStyles.RoundtripKind);
			return new DateTime (dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);
			}

		private static string CreateTempDir ()
			{
			var dir = Path.Combine (Path.GetTempPath (), "ssm-indicators-test-" + Guid.NewGuid ().ToString ("N"));
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
				}
			}
		}
	}
