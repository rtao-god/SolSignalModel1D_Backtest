using SolSignalModel1D_Backtest.Core.Causal.Data;
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

			// Жёсткая валидация входных аргументов:
			// если данные не подготовлены или параметры некорректны
			if (minutes == null)
				{
				// null-коллекция – значит не были подтянуты минутки под указанный горизонт
				throw new ArgumentNullException (
					nameof (minutes),
					"PathLabeler.AssignLabel: minutes collection is null. " +
					"Ensure 1m candles are preloaded for the baseline horizon before calling labeler.");
				}

			if (minutes.Count == 0)
				{
				// Пустая коллекция – также ошибка подготовки данных.
				throw new InvalidOperationException (
					"PathLabeler.AssignLabel: minutes collection is empty. " +
					$"entryUtc={entryUtc:o}. Ensure 1m candles are fetched for the requested horizon.");
				}

			if (entryPrice <= 0.0)
				{
				// Нулевой/отрицательный entryPrice свидетельствует о битых OHLC-данных.
				throw new ArgumentOutOfRangeException (
					nameof (entryPrice),
					entryPrice,
					"PathLabeler.AssignLabel: entryPrice must be > 0. " +
					"Check upstream OHLC data source.");
				}

			if (minMove <= 0.0)
				{
				// Порог движения должен быть положительным, иначе стакан меток некорректен.
				throw new ArgumentOutOfRangeException (
					nameof (minMove),
					minMove,
					"PathLabeler.AssignLabel: minMove must be > 0. " +
					"Check labeling configuration.");
				}

			DateTime endUtc;
			try
				{
				endUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz);
				}
			catch (Exception ex)
				{
				// Явно ломаемся с понятным сообщением
				throw new InvalidOperationException (
					$"Failed to compute baseline exit for entryUtc={entryUtc:o}, tz={NyTz.Id}. " +
					"Fix data/windowing logic instead of relying on fallback.",
					ex);
				}

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
