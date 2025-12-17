using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
	{
	/// <summary>
	/// Строит omniscient BacktestRecord из:
	/// - causal прогнозов,
	/// - truth (LabeledCausalRow),
	/// - полного ряда 1m-свечей (forward-факты).
	///
	/// Инварианты:
	/// - allMinutes строго отсортирован по OpenTimeUtc;
	/// - все свечи валидны (UTC-время, finite-значения, корректный OHLC);
	/// - отрицательные ходы (maxHigh < entry или minLow > entry) считаются ошибкой данных.
	/// </summary>
	public static class ForwardOutcomesBuilder
		{
		public static IReadOnlyList<BacktestRecord> Build (
			IReadOnlyList<CausalPredictionRecord> causalRecords,
			IReadOnlyList<LabeledCausalRow> truthRows,
			IReadOnlyList<Candle1m> allMinutes )
			{
			if (causalRecords == null) throw new ArgumentNullException (nameof (causalRecords));
			if (truthRows == null) throw new ArgumentNullException (nameof (truthRows));
			if (allMinutes == null) throw new ArgumentNullException (nameof (allMinutes));

			if (causalRecords.Count == 0)
				return Array.Empty<BacktestRecord> ();

			if (allMinutes.Count == 0)
				throw new InvalidOperationException ("[forward] 1m-ряд пуст: невозможно посчитать forward-исходы.");

			SeriesGuards.EnsureStrictlyAscendingUtc (allMinutes, m => m.OpenTimeUtc, "ForwardOutcomesBuilder.allMinutes");

			var truthByDate = new Dictionary<DateTime, LabeledCausalRow> (truthRows.Count);
			for (int i = 0; i < truthRows.Count; i++)
				{
				var t = truthRows[i];
				if (!truthByDate.TryAdd (t.DateUtc, t))
					throw new InvalidOperationException ($"[forward] duplicate truth row for date {t.DateUtc:O}.");
				}

			// Сортировка каузальных записей по DateUtc.
			var orderedRecords = causalRecords
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var result = new List<BacktestRecord> (orderedRecords.Count);

			// Скользящий индекс по allMinutes.
			int minuteIndex = 0;

			foreach (var causal in orderedRecords)
				{
				var dayStart = causal.DateUtc;

				if (!truthByDate.TryGetValue (dayStart, out var truth))
					throw new InvalidOperationException ($"[forward] No truth row for causal date {dayStart:O}.");

				if (dayStart.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[forward] causal.DateUtc must be UTC: {dayStart:O}.");

				var dayEnd = Windowing.ComputeBaselineExitUtc (dayStart, Windowing.NyTz);

				if (dayEnd <= dayStart)
					throw new InvalidOperationException ($"[forward] Некорректное baseline-окно: start={dayStart:O}, end={dayEnd:O}.");

				// Сдвиг до первой свечи окна (>= dayStart).
				while (minuteIndex < allMinutes.Count && allMinutes[minuteIndex].OpenTimeUtc < dayStart)
					minuteIndex++;

				var dayMinutes = new List<Candle1m> ();
				int j = minuteIndex;

				// Полуоткрытое окно [dayStart; dayEnd).
				while (j < allMinutes.Count)
					{
					var m = allMinutes[j];
					if (m.OpenTimeUtc >= dayEnd)
						break;

					dayMinutes.Add (m);
					j++;
					}

				if (dayMinutes.Count == 0)
					throw new InvalidOperationException ($"[forward] Не найдены 1m-свечи для окна, начинающегося {dayStart:O}.");

				// Следующий день стартует с конца текущего окна.
				minuteIndex = j;

				// Строгая валидация 1m-свечей окна.
				for (int k = 0; k < dayMinutes.Count; k++)
					ValidateMinuteCandle (dayMinutes[k], dayStart);

				var first = dayMinutes[0];
				if (!double.IsFinite (first.Open) || first.Open <= 0.0)
					throw new InvalidOperationException ($"[forward] Entry-цена должна быть finite и > 0 (день {dayStart:O}).");

				double entry = first.Open;

				double maxHigh = double.NegativeInfinity;
				double minLow = double.PositiveInfinity;

				for (int k = 0; k < dayMinutes.Count; k++)
					{
					var m = dayMinutes[k];

					if (m.High > maxHigh) maxHigh = m.High;
					if (m.Low < minLow) minLow = m.Low;
					}

				if (!double.IsFinite (maxHigh) || maxHigh <= 0.0)
					throw new InvalidOperationException ($"[forward] Некорректный maxHigh в baseline-окне {dayStart:O}.");

				if (!double.IsFinite (minLow) || minLow <= 0.0)
					throw new InvalidOperationException ($"[forward] Некорректный minLow в baseline-окне {dayStart:O}.");

				var last = dayMinutes[dayMinutes.Count - 1];
				if (!double.IsFinite (last.Close) || last.Close <= 0.0)
					throw new InvalidOperationException ($"[forward] Close последней 1m-свечи должен быть finite и > 0 (день {dayStart:O}).");

				double close24 = last.Close;

				double upDiff = maxHigh - entry;
				if (upDiff < 0.0)
					throw new InvalidOperationException (
						$"[forward] maxHigh < entry: entry={entry:0.########}, maxHigh={maxHigh:0.########}, day={dayStart:O}.");

				double downDiff = entry - minLow;
				if (downDiff < 0.0)
					throw new InvalidOperationException (
						$"[forward] minLow > entry: entry={entry:0.########}, minLow={minLow:0.########}, day={dayStart:O}.");

				double upMove = upDiff / entry;
				double downMove = downDiff / entry;

				if (!double.IsFinite (upMove) || !double.IsFinite (downMove))
					throw new InvalidOperationException ($"[forward] Non-finite move computed for day {dayStart:O}.");

				double forwardMinMove = Math.Max (upMove, downMove);

				var forward = new ForwardOutcomes
					{
					DateUtc = dayStart,
					WindowEndUtc = dayEnd,
					Entry = entry,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = close24,
					DayMinutes = dayMinutes,
					MinMove = forwardMinMove,

					TrueLabel = truth.TrueLabel,
					FactMicroUp = truth.FactMicroUp,
					FactMicroDown = truth.FactMicroDown
					};

				result.Add (new BacktestRecord
					{
					Causal = causal,
					Forward = forward
					});
				}

			return result;
			}

		private static void ValidateMinuteCandle ( Candle1m m, DateTime dayStartUtc )
			{
			if (m.OpenTimeUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[forward] Candle1m.OpenTimeUtc must be UTC (day {dayStartUtc:O}).");

			ValidatePrice (m.Open, "Open", dayStartUtc);
			ValidatePrice (m.High, "High", dayStartUtc);
			ValidatePrice (m.Low, "Low", dayStartUtc);
			ValidatePrice (m.Close, "Close", dayStartUtc);

			if (m.High < m.Low)
				throw new InvalidOperationException ($"[forward] Candle1m has High < Low (day {dayStartUtc:O}).");

			// OHLC-ограничения: High покрывает Open/Close, Low покрывает Open/Close.
			if (m.High < m.Open || m.High < m.Close)
				throw new InvalidOperationException ($"[forward] Candle1m has High below Open/Close (day {dayStartUtc:O}).");

			if (m.Low > m.Open || m.Low > m.Close)
				throw new InvalidOperationException ($"[forward] Candle1m has Low above Open/Close (day {dayStartUtc:O}).");
			}

		private static void ValidatePrice ( double v, string name, DateTime dayStartUtc )
			{
			if (!double.IsFinite (v) || v <= 0.0)
				throw new InvalidOperationException ($"[forward] Candle1m.{name} must be finite and > 0 (day {dayStartUtc:O}).");
			}
		}
	}
