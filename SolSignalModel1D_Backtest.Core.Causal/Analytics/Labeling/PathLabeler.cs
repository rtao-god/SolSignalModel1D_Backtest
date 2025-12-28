using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Labeling
	{
	public static class PathLabeler
		{
		public static int AssignLabel (
			in Baseline1mWindow window,
			double entryPrice,
			double minMove,
			out int firstPassDir,
			out DateTime? firstPassTimeUtc,
			out double reachedUpPct,
			out double reachedDownPct,
			out bool ambiguousHitSameMinute )
			{
			firstPassDir = 0;
			firstPassTimeUtc = null;
			reachedUpPct = 0.0;
			reachedDownPct = 0.0;
			ambiguousHitSameMinute = false;

			if (entryPrice <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (entryPrice), entryPrice, "[path-label] entryPrice must be > 0.");

			if (minMove <= 0.0)
				throw new ArgumentOutOfRangeException (nameof (minMove), minMove, "[path-label] minMove must be > 0.");

			if (window.Count <= 0)
				throw new InvalidOperationException ($"[path-label] empty baseline window for entry={window.EntryUtc:O}.");

			double upLevel = entryPrice * (1.0 + minMove);
			double downLevel = entryPrice * (1.0 - minMove);

			double maxHigh = double.MinValue;
			double minLow = double.MaxValue;

			for (int i = 0; i < window.Count; i++)
				{
				Candle1m m = window[i];

				if (m.High > maxHigh) maxHigh = m.High;
				if (m.Low < minLow) minLow = m.Low;

				bool hitUp = m.High >= upLevel;
				bool hitDown = m.Low <= downLevel;

				if (firstPassDir == 0)
					{
					if (hitUp && !hitDown)
						{
						firstPassDir = +1;
						firstPassTimeUtc = m.OpenTimeUtc;
						}
					else if (hitDown && !hitUp)
						{
						firstPassDir = -1;
						firstPassTimeUtc = m.OpenTimeUtc;
						}
					else if (hitUp && hitDown)
						{
						ambiguousHitSameMinute = true;
						firstPassDir = 0;
						firstPassTimeUtc = m.OpenTimeUtc;
						}
					}
				}

			if (maxHigh <= 0.0 || minLow <= 0.0)
				{
				throw new InvalidOperationException (
					$"[path-label] non-positive extremes in window for entry={window.EntryUtc:O}. " +
					$"maxHigh={maxHigh}, minLow={minLow}.");
				}

			reachedUpPct = maxHigh / entryPrice - 1.0;
			reachedDownPct = minLow / entryPrice - 1.0;

			if (firstPassDir > 0) return 2;
			if (firstPassDir < 0) return 0;
			return 1;
			}
		}
	}
