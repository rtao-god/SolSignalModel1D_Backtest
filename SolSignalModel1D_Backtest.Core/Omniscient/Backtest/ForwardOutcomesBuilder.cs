using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
	{
	/// <summary>
	/// Строит omniscient BacktestRecord из:
	/// - causal прогнозов (без доступа к будущему),
	/// - truth (LabeledCausalRow),
	/// - полного ряда 1m-свечей (forward-факты).
	///
	/// Инварианты:
	/// - allMinutes должен быть строго отсортирован по OpenTimeUtc (это делается на бутстрапе один раз);
	/// - внутри билдера повторная сортировка запрещена: если порядок нарушен — это ошибка данных/пайплайна.
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

			// causalRecords могут приходить неотсортированными — сортировка дешёвая (N~1k) и безопасная.
			var orderedRecords = causalRecords
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var result = new List<BacktestRecord> (orderedRecords.Count);

			// Скользящий индекс: окна дневных записей возрастают => линейный проход по 1m без бинарных поисков.
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

				// Сдвигаем minuteIndex до первой свечи окна (>= dayStart).
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

				// Следующий день начнёт поиск с конца текущего окна.
				minuteIndex = j;

				var first = dayMinutes[0];
				if (first.Open <= 0.0)
					throw new InvalidOperationException ($"[forward] Entry-цена должна быть положительной (день {dayStart:O}).");

				double entry = first.Open;

				double maxHigh = double.MinValue;
				double minLow = double.MaxValue;

				foreach (var m in dayMinutes)
					{
					// High/Low должны быть положительными в нормальных данных.
					// Нули/минуса — не “лечим”, это сигнал порчи/дырок в источнике.
					if (m.High > 0.0 && m.High > maxHigh)
						maxHigh = m.High;

					if (m.Low > 0.0 && m.Low < minLow)
						minLow = m.Low;
					}

				if (maxHigh <= 0.0 || minLow <= 0.0)
					throw new InvalidOperationException ($"[forward] Некорректные High/Low в baseline-окне {dayStart:O}.");

				var last = dayMinutes[dayMinutes.Count - 1];
				double close24 = last.Close;

				double upMove = (maxHigh - entry) / entry;
				double downMove = (entry - minLow) / entry;

				// Отрицательные значения возможны только из-за неконсистентных цен.
				// Не маскируем “дефолтом”, но clamp к 0 оставляем как числовую стабилизацию метрики хода.
				if (upMove < 0.0) upMove = 0.0;
				if (downMove < 0.0) downMove = 0.0;

				double forwardMinMove = Math.Max (upMove, downMove);

				// Truth по архитектуре живёт в Forward (BacktestRecord читает TrueLabel/FactMicro* именно оттуда),
				// чтобы каузальный слой физически не мог “подсмотреть” истину будущего окна.
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
		}
	}
