using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Causal.Infra;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps
	{
	/// <summary>
	/// Лёгкий NDJSON-журнал дыр в свечах.
	/// Идея: реестр known gaps фиксируется кодом, а журнал отражает фактические попадания в окна/дни.
	/// </summary>
	public sealed class CandleGapJournal
		{
		private readonly string _path;

		public CandleGapJournal ( string symbol, string interval )
			{
			if (string.IsNullOrWhiteSpace (symbol)) throw new ArgumentException ("symbol empty", nameof (symbol));
			if (string.IsNullOrWhiteSpace (interval)) throw new ArgumentException ("interval empty", nameof (interval));

			symbol = symbol.Trim ().ToUpperInvariant ();

			var dir = Path.Combine (PathConfig.CandlesDir, "_gaps");
			Directory.CreateDirectory (dir);

			_path = Path.Combine (dir, $"{symbol}-{interval}.gaps.ndjson");
			}

		private sealed class Line
			{
			public string Kind { get; set; } = "candle-gap-hit";

			public DateTime LoggedAtUtc { get; set; }

			public string Symbol { get; set; } = null!;
			public string Interval { get; set; } = null!;

			public DateTime DayUtc { get; set; }

			public DateTime WindowStartUtc { get; set; }
			public DateTime WindowEndUtcExclusive { get; set; }

			public DateTime ExpectedStartUtc { get; set; }
			public DateTime ActualStartUtc { get; set; }

			public int MissingBars { get; set; }

			public bool IsKnown { get; set; }
			public string Action { get; set; } = null!;
			}

		public void AppendSkipDay (
			string symbol,
			string interval,
			DateTime dayUtc,
			DateTime windowStartUtc,
			DateTime windowEndUtcExclusive,
			DateTime expectedStartUtc,
			DateTime actualStartUtc,
			int missingBars,
			bool isKnown )
			{
			var line = new Line
				{
				LoggedAtUtc = DateTime.UtcNow,

				Symbol = symbol,
				Interval = interval,

				DayUtc = dayUtc,

				WindowStartUtc = windowStartUtc,
				WindowEndUtcExclusive = windowEndUtcExclusive,

				ExpectedStartUtc = expectedStartUtc,
				ActualStartUtc = actualStartUtc,
				MissingBars = missingBars,

				IsKnown = isKnown,
				Action = "skip-day"
				};

			var json = JsonSerializer.Serialize (line);

			File.AppendAllText (
				_path,
				json + Environment.NewLine,
				System.Text.Encoding.UTF8);
			}
		}
	}
