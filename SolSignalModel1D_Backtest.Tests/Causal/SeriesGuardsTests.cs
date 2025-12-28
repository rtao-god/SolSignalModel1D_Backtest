using SolSignalModel1D_Backtest.Core.Causal.Utils;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Causal
	{
	public sealed class SeriesGuardsTests
		{
		[Fact]
		public void EnsureNonEmpty_Throws_OnEmpty ()
			{
			var ex = Assert.Throws<InvalidOperationException> (() =>
				SeriesGuards.EnsureNonEmpty (new List<int> (), "empty-series"));

			Assert.Contains ("empty series", ex.Message, StringComparison.OrdinalIgnoreCase);
			}

		[Fact]
		public void EnsureStrictlyAscendingUtc_Throws_OnNonUtcKey ()
			{
			var xs = new List<DateTime>
				{
				new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
				};

			var ex = Assert.Throws<InvalidOperationException> (() =>
				SeriesGuards.EnsureStrictlyAscendingUtc (xs, x => x, "non-utc"));

			Assert.Contains ("must be UTC", ex.Message, StringComparison.OrdinalIgnoreCase);
			}

		[Fact]
		public void EnsureStrictlyAscendingUtc_Throws_OnDuplicateKey ()
			{
			var t = new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var xs = new List<DateTime> { t, t };

			var ex = Assert.Throws<InvalidOperationException> (() =>
				SeriesGuards.EnsureStrictlyAscendingUtc (xs, x => x, "dup"));

			Assert.Contains ("not strictly ascending", ex.Message, StringComparison.OrdinalIgnoreCase);
			}

		[Fact]
		public void SortByKeyUtcInPlace_SortsAndValidates ()
			{
			var xs = new List<Row>
				{
				new Row (new DateTime (2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
				new Row (new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
				new Row (new DateTime (2024, 1, 3, 0, 0, 0, DateTimeKind.Utc))
				};

			SeriesGuards.SortByKeyUtcInPlace (xs, x => x.Utc, "rows");

			Assert.Equal (new DateTime (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), xs[0].Utc);
			Assert.Equal (new DateTime (2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), xs[1].Utc);
			Assert.Equal (new DateTime (2024, 1, 3, 0, 0, 0, DateTimeKind.Utc), xs[2].Utc);
			}

		private sealed class Row
			{
			public Row ( DateTime utc )
				{
				Utc = utc;
				}

			public DateTime Utc { get; }
			}
		}
	}
