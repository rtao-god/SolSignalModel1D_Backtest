using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
	{
	/// <summary>
	/// Строит BacktestRecord из:
	/// - списка CausalPredictionRecord (без доступа к будущему);
	/// - полного ряда 1m-свечей.
	/// Здесь концентрируется весь расчёт forward-характеристик.
	/// </summary>
	public static class ForwardOutcomesBuilder
		{
		/// <summary>
		/// Построить BacktestRecord для всех дней на основе causal-прогнозов и 1m-свечей.
		/// Предполагается, что DateUtc у записей уже соответствует старту baseline-окна.
		/// </summary>
		public static IReadOnlyList<BacktestRecord> Build (
			IReadOnlyList<CausalPredictionRecord> causalRecords,
			IReadOnlyList<Candle1m> allMinutes )
			{
			if (causalRecords == null) throw new ArgumentNullException (nameof (causalRecords));
			if (allMinutes == null) throw new ArgumentNullException (nameof (allMinutes));

			if (causalRecords.Count == 0)
				return Array.Empty<BacktestRecord> ();

			var orderedRecords = causalRecords
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var orderedMinutes = allMinutes
				.OrderBy (m => m.OpenTimeUtc)
				.ToList ();

			if (orderedMinutes.Count == 0)
				throw new InvalidOperationException ("[forward] 1m-ряд пуст: невозможно посчитать forward-исходы.");

			var result = new List<BacktestRecord> (orderedRecords.Count);

			int minuteIndex = 0;

			foreach (var causal in orderedRecords)
				{
				var dayStart = causal.DateUtc;
				var dayEnd = Windowing.ComputeBaselineExitUtc (dayStart);

				if (dayEnd <= dayStart)
					throw new InvalidOperationException (
						$"[forward] Некорректное baseline-окно: start={dayStart:O}, end={dayEnd:O}.");

				// Продвигаем общий указатель по 1m-ряду до начала окна.
				while (minuteIndex < orderedMinutes.Count &&
					   orderedMinutes[minuteIndex].OpenTimeUtc < dayStart)
					{
					minuteIndex++;
					}

				var dayMinutes = new List<Candle1m> ();

				int j = minuteIndex;
				while (j < orderedMinutes.Count)
					{
					var m = orderedMinutes[j];
					if (m.OpenTimeUtc >= dayEnd)
						break;

					if (m.OpenTimeUtc >= dayStart)
						dayMinutes.Add (m);

					j++;
					}

				if (dayMinutes.Count == 0)
					{
					throw new InvalidOperationException (
						$"[forward] Не найдены 1m-свечи для окна, начинающегося {dayStart:O}.");
					}

				minuteIndex = j;

				var first = dayMinutes[0];
				if (first.Open <= 0.0)
					{
					throw new InvalidOperationException (
						$"[forward] Entry-цена должна быть положительной (день {dayStart:O}).");
					}

				double entry = first.Open;

				double maxHigh = double.MinValue;
				double minLow = double.MaxValue;

				foreach (var m in dayMinutes)
					{
					if (m.High > 0.0 && m.High > maxHigh)
						maxHigh = m.High;

					if (m.Low > 0.0 && m.Low < minLow)
						minLow = m.Low;
					}

				if (maxHigh <= 0.0 || minLow <= 0.0)
					{
					throw new InvalidOperationException (
						$"[forward] Некорректные High/Low в baseline-окне {dayStart:O}.");
					}

				var last = dayMinutes[dayMinutes.Count - 1];
				double close24 = last.Close;

				// Простая волатильность: максимальный ход в любую сторону от entry.
				double upMove = (maxHigh - entry) / entry;
				double downMove = (entry - minLow) / entry;
				if (upMove < 0.0) upMove = 0.0;
				if (downMove < 0.0) downMove = 0.0;

				double minMove = Math.Max (upMove, downMove);

				var forward = new ForwardOutcomes
					{
					DateUtc = dayStart,
					WindowEndUtc = dayEnd,
					Entry = entry,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = close24,
					DayMinutes = dayMinutes,
					MinMove = minMove
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
