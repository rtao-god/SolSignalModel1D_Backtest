using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Analytics.Labeling
	{
	public readonly struct Baseline1mWindow
		{
		private readonly IReadOnlyList<Candle1m> _all;

		public DateTime EntryUtc { get; }
		public DateTime ExitUtcExclusive { get; }
		public int StartIdx { get; }
		public int EndIdxExclusive { get; }
		public int Count => EndIdxExclusive - StartIdx;

		private Baseline1mWindow (
			IReadOnlyList<Candle1m> all,
			DateTime entryUtc,
			DateTime exitUtcExclusive,
			int startIdx,
			int endIdxExclusive )
			{
			_all = all;
			EntryUtc = entryUtc;
			ExitUtcExclusive = exitUtcExclusive;
			StartIdx = startIdx;
			EndIdxExclusive = endIdxExclusive;
			}

		public Candle1m this[int offset]
			{
			get
				{
				if ((uint) offset >= (uint) Count)
					throw new ArgumentOutOfRangeException (nameof (offset), $"offset={offset}, Count={Count}.");
				return _all[StartIdx + offset];
				}
			}

		public static Baseline1mWindow CreateForBaseline (
			IReadOnlyList<Candle1m> allMinutesSortedUtc,
			DateTime entryUtc,
			TimeZoneInfo nyTz )
			{
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));
			var exitUtcExclusive = NyWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			return Create (allMinutesSortedUtc, entryUtc, exitUtcExclusive);
			}

		public static Baseline1mWindow Create (
			IReadOnlyList<Candle1m> allMinutesSortedUtc,
			DateTime entryUtc,
			DateTime exitUtcExclusive )
			{
			if (allMinutesSortedUtc == null) throw new ArgumentNullException (nameof (allMinutesSortedUtc));
			if (allMinutesSortedUtc.Count == 0)
				throw new InvalidOperationException ("[baseline-1m] minutes collection is empty.");

			if (entryUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[baseline-1m] entryUtc must be UTC, got Kind={entryUtc.Kind}, t={entryUtc:O}.");
			if (exitUtcExclusive.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[baseline-1m] exitUtcExclusive must be UTC, got Kind={exitUtcExclusive.Kind}, t={exitUtcExclusive:O}.");
			if (exitUtcExclusive <= entryUtc)
				throw new InvalidOperationException ($"[baseline-1m] invalid window: exitUtcExclusive <= entryUtc. entry={entryUtc:O}, exit={exitUtcExclusive:O}.");

			if (allMinutesSortedUtc[0].OpenTimeUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[baseline-1m] minutes must be UTC (first.Kind={allMinutesSortedUtc[0].OpenTimeUtc.Kind}).");
			if (allMinutesSortedUtc[^1].OpenTimeUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[baseline-1m] minutes must be UTC (last.Kind={allMinutesSortedUtc[^1].OpenTimeUtc.Kind}).");

			int startIdx = LowerBoundOpenTimeUtc (allMinutesSortedUtc, entryUtc);
			int endIdxExclusive = LowerBoundOpenTimeUtc (allMinutesSortedUtc, exitUtcExclusive);

			if (startIdx >= allMinutesSortedUtc.Count)
				{
				throw new InvalidOperationException (
					$"[baseline-1m] no minutes coverage for entryUtc={entryUtc:O}. " +
					$"minutes=[{allMinutesSortedUtc[0].OpenTimeUtc:O}..{allMinutesSortedUtc[^1].OpenTimeUtc:O}].");
				}

			// Вход в окно должен попадать ровно на минутную свечу.
			// Иначе таргет/экстремумы считаются по урезанному пути и становятся недетерминированными.
			var startT = allMinutesSortedUtc[startIdx].OpenTimeUtc;
			if (startT != entryUtc)
				{
				throw new InvalidOperationException (
					$"[baseline-1m] entry minute is missing: expected={entryUtc:O}, actual={startT:O}, idx={startIdx}.");
				}

			if (endIdxExclusive <= startIdx)
				{
				throw new InvalidOperationException (
					$"[baseline-1m] no minutes in window [entryUtc; exitUtcExclusive). " +
					$"entry={entryUtc:O}, exit={exitUtcExclusive:O}, range=[{startIdx}; {endIdxExclusive}).");
				}

			return new Baseline1mWindow (allMinutesSortedUtc, entryUtc, exitUtcExclusive, startIdx, endIdxExclusive);
			}

		private static int LowerBoundOpenTimeUtc ( IReadOnlyList<Candle1m> all1m, DateTime tUtc )
			{
			int lo = 0;
			int hi = all1m.Count;

			while (lo < hi)
				{
				int mid = lo + ((hi - lo) >> 1);
				if (all1m[mid].OpenTimeUtc < tUtc)
					lo = mid + 1;
				else
					hi = mid;
				}

			return lo;
			}
		}
	}
