using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Analytics.Labeling
	{
	public static class PathLabeler
		{
		/// <summary>
		/// Path-based факт-класс за базовый горизонт:
		/// entryUtc / entryPrice – точка входа (6h close),
		/// minMove – порог в долях (0.02 = 2%),
		/// minutes – минутки SOL по всему диапазону.
		/// Горизонт берём не "тупо +24 часа", а до следующего
		/// рабочего NY-утра (через Windowing.ComputeBaselineExitUtc).
		/// </summary>
		
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public static int AssignLabel (
			DateTime entryUtc,
			double entryPrice,
			double minMove,
			IReadOnlyList<Candle1m> minutes,
			out int firstPassDir,
			out DateTime? firstPassTimeUtc,
			out double reachedUpPct,
			out double reachedDownPct )
			{
			firstPassDir = 0;
			firstPassTimeUtc = null;
			reachedUpPct = 0.0;
			reachedDownPct = 0.0;

			// базовая защита
			if (minutes == null || minutes.Count == 0 || entryPrice <= 0.0 || minMove <= 0.0)
				return 1; // flat по умолчанию

			DateTime endUtc;			
				endUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz: NyTz);

			var dayMins = minutes
				.Where (m => m.OpenTimeUtc >= entryUtc && m.OpenTimeUtc < endUtc)
				.ToList ();

			if (dayMins.Count == 0)
				return 1;

			double maxHigh = double.MinValue;
			double minLow = double.MaxValue;

			double upLevel = entryPrice * (1.0 + minMove);
			double downLevel = entryPrice * (1.0 - minMove);

			foreach (var m in dayMins)
				{
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
						// оба могли случиться в одном баре – считаем “боковик, оба за бар”
						firstPassDir = 0;
						firstPassTimeUtc = m.OpenTimeUtc;
						}
					}
				}

			if (maxHigh <= 0 || minLow <= 0)
				return 1;

			reachedUpPct = maxHigh / entryPrice - 1.0;
			reachedDownPct = minLow / entryPrice - 1.0; // отрицательный

			if (firstPassDir > 0) return 2; // up
			if (firstPassDir < 0) return 0; // down
			return 1; // flat
			}
		}
	}
