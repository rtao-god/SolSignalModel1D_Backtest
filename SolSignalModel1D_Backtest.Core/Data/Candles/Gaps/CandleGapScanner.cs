using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Candles.Gaps
	{
	/// <summary>
	/// Детерминированный сканер дыр в минутной серии.
	/// Контракт:
	/// - серия должна быть строго возрастающей по OpenTimeUtc (дубликаты/обратный ход считаются повреждением данных);
	/// - дыра фиксируется как интервал отсутствующих баров [ExpectedStartUtc..ActualStartUtc).
	/// </summary>
	public static class CandleGapScanner
		{
		public readonly struct DetectedGap
			{
			public DetectedGap ( string symbol, string interval, DateTime expectedStartUtc, DateTime actualStartUtc )
				{
				Symbol = symbol;
				Interval = interval;
				ExpectedStartUtc = expectedStartUtc;
				ActualStartUtc = actualStartUtc;
				}

			public string Symbol { get; }
			public string Interval { get; }
			public DateTime ExpectedStartUtc { get; }
			public DateTime ActualStartUtc { get; }

			public DateTime MissingFromUtc => ExpectedStartUtc;
			public DateTime MissingToUtc => ActualStartUtc;

			public int MissingBars1m
				{
				get
					{
					var span = ActualStartUtc - ExpectedStartUtc;
					var mins = (int) Math.Round (span.TotalMinutes);
					return mins < 0 ? 0 : mins;
					}
				}
			}

		public static List<DetectedGap> Scan1mGaps (
			IReadOnlyList<Candle1m> all1mSortedUtc,
			string symbol,
			string seriesName )
			{
			if (all1mSortedUtc == null) throw new ArgumentNullException (nameof (all1mSortedUtc));
			if (all1mSortedUtc.Count == 0) return new List<DetectedGap> (0);
			if (string.IsNullOrWhiteSpace (symbol)) throw new ArgumentException ("symbol is empty", nameof (symbol));
			if (string.IsNullOrWhiteSpace (seriesName)) seriesName = "1m-series";

			symbol = symbol.Trim ().ToUpperInvariant ();

			var res = new List<DetectedGap> ();

			var prev = all1mSortedUtc[0].OpenTimeUtc;
			if (prev.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[gaps] {seriesName}: OpenTimeUtc must be UTC, first={prev:O} Kind={prev.Kind}.");

			for (int i = 1; i < all1mSortedUtc.Count; i++)
				{
				var cur = all1mSortedUtc[i].OpenTimeUtc;
				if (cur.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[gaps] {seriesName}: OpenTimeUtc must be UTC, i={i}, t={cur:O} Kind={cur.Kind}.");

				if (cur <= prev)
					{
					throw new InvalidOperationException (
						$"[gaps] {seriesName}: non-strictly ascending minutes at i={i}. prev={prev:O}, cur={cur:O}.");
					}

				var expected = prev.AddMinutes (1);
				if (cur != expected)
					{
					res.Add (new DetectedGap (
						symbol: symbol,
						interval: "1m",
						expectedStartUtc: expected,
						actualStartUtc: cur));
					}

				prev = cur;
				}

			return res;
			}

		public static bool OverlapsWindow (
			in DetectedGap gap,
			DateTime windowStartUtc,
			DateTime windowEndUtcExclusive )
			{
			return gap.MissingFromUtc < windowEndUtcExclusive && gap.MissingToUtc > windowStartUtc;
			}
		}
	}
